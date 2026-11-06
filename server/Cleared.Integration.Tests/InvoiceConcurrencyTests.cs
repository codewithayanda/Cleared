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

    // Changing a draft rewrites its lines, so a change that lands after the issue would alter an invoice whose
    // total has already been fixed and sent. Each change prices the draft differently, so one that slips in
    // after the issue shows up as a total that is not the one the issue reported.
    [Fact]
    public async Task Changing_a_draft_while_it_is_issued_never_alters_the_invoice_after_the_issue()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Race Customer");

        for (var round = 0; round < 5; round++)
        {
            var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
                "api/v1/invoices", TestData.NewInvoice(customer.Id));
            var sends = new List<Func<Task<HttpResponseMessage>>>
            {
                () => owner.Client.PostAsJsonAsync(
                    $"api/v1/invoices/{draft.Id}/issue", TestData.IssueToday(), ApiJson.Options),
            };
            sends.AddRange(Enumerable.Range(1, 6).Select<int, Func<Task<HttpResponseMessage>>>(price => () =>
                owner.Client.PutAsJsonAsync(
                    $"api/v1/invoices/{draft.Id}", PricedAt(customer.Id, 100 + price), ApiJson.Options)));

            var responses = await FireAllAsync(sends);

            Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
            Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
            Assert.All(
                responses.Skip(1),
                r => Assert.True(
                    r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                    $"A change answered {(int)r.StatusCode}."));

            var reported = await responses[0].Content.ReadFromJsonAsync<InvoiceResponse>(ApiJson.Options);
            var stored = await owner.Client.GetFromJsonAsync<InvoiceResponse>(
                $"api/v1/invoices/{draft.Id}", ApiJson.Options);
            Assert.Equal(reported!.Total, stored!.Total);
            Assert.Equal(
                reported.Lines.Select(l => $"{l.Description} {l.Quantity} {l.UnitPrice}"),
                stored.Lines.Select(l => $"{l.Description} {l.Quantity} {l.UnitPrice}"));
        }
    }

    // Issuing takes the next number, so a draft that is issued and also deleted would leave a number with no
    // invoice behind it, which a VAT invoice sequence must never do. One of the two has to win and the
    // other has to find that it lost.
    [Fact]
    public async Task Deleting_a_draft_while_it_is_issued_never_does_both_and_leaves_no_gap()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Race Customer");
        var numbers = new List<string?>();

        for (var round = 0; round < 6; round++)
        {
            var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
                "api/v1/invoices", TestData.NewInvoice(customer.Id));

            var responses = await FireAllAsync(
            [
                () => owner.Client.PostAsJsonAsync(
                    $"api/v1/invoices/{draft.Id}/issue", TestData.IssueToday(), ApiJson.Options),
                () => owner.Client.DeleteAsync($"api/v1/invoices/{draft.Id}"),
            ]);

            var issue = responses[0].StatusCode;
            var delete = responses[1].StatusCode;
            var issuedFirst = issue == HttpStatusCode.OK && delete == HttpStatusCode.Conflict;
            var deletedFirst = delete == HttpStatusCode.NoContent && issue == HttpStatusCode.NotFound;
            Assert.True(issuedFirst || deletedFirst, $"The issue answered {(int)issue} and the delete {(int)delete}.");

            using var read = await owner.Client.GetAsync($"api/v1/invoices/{draft.Id}");
            Assert.Equal(issuedFirst ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);

            if (issuedFirst)
            {
                numbers.Add((await responses[0].Content.ReadFromJsonAsync<InvoiceResponse>(ApiJson.Options))!.Number);
            }
        }

        var year = TestData.Today.Year;
        Assert.Equal(
            Enumerable.Range(1, numbers.Count).Select(n => $"INV-{year}-{n:D4}"),
            numbers);
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

    private static UpdateInvoiceRequest PricedAt(Guid customerId, int price) =>
        new(customerId, [new CreateInvoiceLineRequest("Consulting", 1m, $"{price}.00")]);

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
