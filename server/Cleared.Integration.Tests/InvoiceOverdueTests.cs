using System.Net.Http.Json;
using Cleared.Application.CreditNotes;
using Cleared.Application.Invoices;

namespace Cleared.Integration.Tests;

// Overdue is worked out when an invoice is read and never stored, so every way of reading one is checked.
// The due dates here are days either side of today, never today itself: the API judges by the date in
// South Africa and the tests by the UTC date, and those differ for two hours of every day.
[Collection(ApiCollection.Name)]
public class InvoiceOverdueTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task An_unpaid_invoice_past_its_due_date_is_overdue_wherever_it_is_read()
    {
        var (owner, issued) = await PastDueInvoiceAsync();

        var fetched = await owner.Client.GetFromJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{issued.Id}", ApiJson.Options);

        Assert.True(issued.IsOverdue);
        Assert.True(fetched!.IsOverdue);
        Assert.True((await ListedAsync(owner, issued)).IsOverdue);
    }

    [Fact]
    public async Task An_invoice_that_is_not_yet_due_is_not_overdue()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");

        var issued = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        Assert.False(issued.IsOverdue);
        Assert.False((await ListedAsync(owner, issued)).IsOverdue);
    }

    [Fact]
    public async Task A_part_payment_leaves_an_invoice_overdue_and_paying_it_off_clears_it()
    {
        var (owner, issued) = await PastDueInvoiceAsync();

        await owner.Client.RecordPaymentAsync(issued.Id, "400.00");
        var afterPart = await ListedAsync(owner, issued);
        await owner.Client.RecordPaymentAsync(issued.Id, "600.00");
        var afterAll = await ListedAsync(owner, issued);

        Assert.Equal("PartiallyPaid", afterPart.Status);
        Assert.True(afterPart.IsOverdue);
        Assert.Equal("Paid", afterAll.Status);
        Assert.False(afterAll.IsOverdue);
    }

    [Fact]
    public async Task A_cancelled_invoice_is_not_overdue()
    {
        var (owner, issued) = await PastDueInvoiceAsync();

        await owner.Client.PostJsonAsync<CreditNoteResponse>(
            $"api/v1/invoices/{issued.Id}/credit-notes",
            new CreateCreditNoteRequest(
                "Cancelled by the customer", [new CreateCreditNoteLineRequest(issued.Lines[0].Id, 2)]));
        var listed = await ListedAsync(owner, issued);

        Assert.Equal("Cancelled", listed.Status);
        Assert.False(listed.IsOverdue);
    }

    private async Task<(TestTenant Owner, InvoiceResponse Issued)> PastDueInvoiceAsync()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var issued = await owner.Client.CreateIssuedInvoiceAsync(
            customer.Id, new IssueInvoiceRequest(TestData.Today.AddDays(-40), TestData.Today.AddDays(-10)));

        return (owner, issued);
    }

    private static async Task<InvoiceResponse> ListedAsync(TestTenant owner, InvoiceResponse invoice)
    {
        var all = await owner.Client.GetFromJsonAsync<List<InvoiceResponse>>("api/v1/invoices", ApiJson.Options);

        return all!.Single(listed => listed.Id == invoice.Id);
    }
}
