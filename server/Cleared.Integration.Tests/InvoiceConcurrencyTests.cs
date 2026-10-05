using System.Net;
using System.Net.Http.Json;
using Cleared.Application.CreditNotes;
using Cleared.Application.Invoices;
using Cleared.Application.Payments;

namespace Cleared.Integration.Tests;

// Fires one request from many callers released together, then checks the invoice stayed
// consistent. Each case fails without the row lock taken through IInvoiceLock.
[Collection(ApiCollection.Name)]
public class InvoiceConcurrencyTests(ClearedApiFactory factory)
{
    private const int Callers = 12;

    [Fact]
    public async Task Concurrent_payments_cannot_overpay_an_invoice()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var payment = new RecordPaymentRequest("600.00", TestData.Today, null);

        // R1000 invoice, R600 payments: any two together overpay it.
        var responses = await FireAsync(() => owner.Client.PostAsJsonAsync(
            $"api/v1/invoices/{invoice.Id}/payments", payment, ApiJson.Options));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var payments = await owner.Client.GetFromJsonAsync<List<PaymentResponse>>(
            $"api/v1/invoices/{invoice.Id}/payments", ApiJson.Options);
        Assert.Single(payments!);

        var after = await owner.Client.GetFromJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{invoice.Id}", ApiJson.Options);
        Assert.Equal("600.00", after!.AmountPaid);
        Assert.Equal("PartiallyPaid", after.Status);
    }

    [Fact]
    public async Task Concurrent_credit_notes_cannot_credit_more_than_was_invoiced()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var credit = new CreateCreditNoteRequest(
            "Returned", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 1.5m)]);

        // 2 units were invoiced, so only one credit of 1.5 can fit.
        var responses = await FireAsync(() => owner.Client.PostAsJsonAsync(
            $"api/v1/invoices/{invoice.Id}/credit-notes", credit, ApiJson.Options));

        var created = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));

        var note = await created.Content.ReadFromJsonAsync<CreditNoteResponse>(ApiJson.Options);
        Assert.Equal(1.5m, note!.Lines.Sum(l => l.Quantity));
    }

    [Fact]
    public async Task Issuing_one_draft_concurrently_issues_it_once_and_leaves_no_gap()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Race Customer");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", TestData.NewInvoice(customer.Id));

        var responses = await FireAsync(() => owner.Client.PostAsJsonAsync(
            $"api/v1/invoices/{draft.Id}/issue", TestData.IssueToday(), ApiJson.Options));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var year = TestData.Today.Year;
        var issued = await owner.Client.GetFromJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{draft.Id}", ApiJson.Options);
        Assert.Equal($"INV-{year}-0001", issued!.Number);

        var next = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);
        Assert.Equal($"INV-{year}-0002", next.Number);
    }

    [Fact]
    public async Task Payments_and_credit_notes_racing_on_one_invoice_never_error_or_overshoot()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        var payment = new RecordPaymentRequest("100.00", TestData.Today, null);
        var credit = new CreateCreditNoteRequest(
            "Returned", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 0.5m)]);

        var calls = Enumerable.Range(0, 6).SelectMany(_ => new (bool IsPayment, Func<Task<HttpResponseMessage>> Send)[]
        {
            (true, () => owner.Client.PostAsJsonAsync($"api/v1/invoices/{invoice.Id}/payments", payment, ApiJson.Options)),
            (false, () => owner.Client.PostAsJsonAsync($"api/v1/invoices/{invoice.Id}/credit-notes", credit, ApiJson.Options)),
        }).ToList();

        var responses = await FireAllAsync(calls.Select(c => c.Send));

        // A deadlock between the two paths would surface here as a 500.
        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

        var paid = 0m;
        var credited = 0m;
        for (var i = 0; i < responses.Count; i++)
        {
            if (responses[i].StatusCode != HttpStatusCode.Created)
            {
                continue;
            }

            if (calls[i].IsPayment)
            {
                var created = await responses[i].Content.ReadFromJsonAsync<PaymentResponse>(ApiJson.Options);
                paid += decimal.Parse(created!.Amount, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                var created = await responses[i].Content.ReadFromJsonAsync<CreditNoteResponse>(ApiJson.Options);
                credited += created!.Lines.Sum(l => l.Quantity);
            }
        }

        Assert.True(credited <= 2m, $"Credited {credited} of the 2 units invoiced.");
        Assert.True(paid <= 1000m, $"Collected R{paid} against a R1000 invoice.");

        var after = await owner.Client.GetFromJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{invoice.Id}", ApiJson.Options);
        Assert.Equal(paid.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), after!.AmountPaid);
    }

    private async Task<(TestTenant Owner, InvoiceResponse Invoice)> IssuedInvoiceAsync()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Race Customer");

        return (owner, await owner.Client.CreateIssuedInvoiceAsync(customer.Id));
    }

    private static Task<IReadOnlyList<HttpResponseMessage>> FireAsync(Func<Task<HttpResponseMessage>> send) =>
        FireAllAsync(Enumerable.Repeat(send, Callers));

    // Releases every call at the same moment, and returns the responses in the order given.
    private static async Task<IReadOnlyList<HttpResponseMessage>> FireAllAsync(
        IEnumerable<Func<Task<HttpResponseMessage>>> sends)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = sends.Select(async send =>
        {
            await gate.Task;
            return await send();
        }).ToList();

        gate.SetResult();

        return await Task.WhenAll(calls);
    }
}
