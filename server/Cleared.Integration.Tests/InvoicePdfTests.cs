using System.Globalization;
using System.Net.Http.Json;
using Cleared.Application.CreditNotes;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;

namespace Cleared.Integration.Tests;

// Reads the PDF the API sends back, the way a customer would. The invoice and its payments come out of
// the database and the page is really drawn, so this checks what the page says from end to end.
[Collection(ApiCollection.Name)]
public class InvoicePdfTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task An_unpaid_invoice_says_total_due_and_shows_how_to_pay()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();

        var text = await PdfTextAsync(owner, invoice);

        Assert.Contains("Total due R 1000.00", text);
        Assert.Contains("BANKING DETAILS", text);
        Assert.Contains($"Payment reference: {invoice.Number}", text);
        Assert.DoesNotContain("Payments received", text);
        Assert.DoesNotContain("PAID", text);
    }

    [Fact]
    public async Task A_part_paid_invoice_lists_what_was_paid_and_asks_only_for_the_balance()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        await owner.Client.RecordPaymentAsync(invoice.Id, "400.00");

        var text = await PdfTextAsync(owner, invoice);

        Assert.Contains("Invoice total R 1000.00", text);
        Assert.Contains("Payments received as at", text);
        Assert.Contains($"{Day(TestData.Today)} R 400.00", text);
        Assert.Contains("Balance due R 600.00", text);
        Assert.Contains("BANKING DETAILS", text);
        Assert.DoesNotContain("Total due", text);
    }

    [Fact]
    public async Task Payments_are_listed_oldest_first_whatever_order_they_were_entered_in()
    {
        var today = TestData.Today;
        var (owner, invoice) = await IssuedInvoiceAsync(new IssueInvoiceRequest(today.AddDays(-20), today.AddDays(10)));
        await owner.Client.RecordPaymentAsync(invoice.Id, "300.00", today.AddDays(-5));
        await owner.Client.RecordPaymentAsync(invoice.Id, "200.00", today.AddDays(-10));

        var text = await PdfTextAsync(owner, invoice);

        // The leading space keeps "5 Oct" from matching inside "25 Oct".
        var older = text.IndexOf($" {Day(today.AddDays(-10))} R 200.00", StringComparison.Ordinal);
        var newer = text.IndexOf($" {Day(today.AddDays(-5))} R 300.00", StringComparison.Ordinal);
        Assert.True(older >= 0 && newer >= 0, text);
        Assert.True(older < newer, text);
    }

    [Fact]
    public async Task A_paid_invoice_is_marked_paid_and_no_longer_asks_for_money()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        await owner.Client.RecordPaymentAsync(invoice.Id, "1000.00");

        var text = await PdfTextAsync(owner, invoice);

        Assert.Contains("PAID", text);
        Assert.Contains("Paid in full", text);
        Assert.DoesNotContain("Paid in full R", text);
        Assert.Contains("Invoice total R 1000.00", text);
        Assert.DoesNotContain("Total due", text);
        Assert.DoesNotContain("Balance due", text);
        Assert.DoesNotContain("BANKING DETAILS", text);
    }

    [Fact]
    public async Task A_cancelled_invoice_is_marked_cancelled_and_no_longer_asks_for_money()
    {
        var (owner, invoice) = await IssuedInvoiceAsync();
        await owner.Client.PostJsonAsync<CreditNoteResponse>(
            $"api/v1/invoices/{invoice.Id}/credit-notes",
            new CreateCreditNoteRequest(
                "Cancelled by the customer", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 2)]));

        var text = await PdfTextAsync(owner, invoice);

        Assert.Contains("CANCELLED", text);
        Assert.DoesNotContain("Cancelled R", text);
        Assert.DoesNotContain("Total due", text);
        Assert.DoesNotContain("BANKING DETAILS", text);
    }

    private static string Day(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    // The company has bank details, so an invoice that is still owed can say how to pay it.
    private async Task<(TestTenant Owner, InvoiceResponse Invoice)> IssuedInvoiceAsync(IssueInvoiceRequest? issue = null)
    {
        var owner = await factory.CreateTenantAsync();
        using var profile = await owner.Client.PutAsJsonAsync(
            "api/v1/tenants/me",
            new UpdateTenantProfileRequest("1 Acme Way, Cape Town", "FNB", "62123456789", "250655"),
            ApiJson.Options);
        profile.EnsureSuccessStatusCode();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");

        return (owner, await owner.Client.CreateIssuedInvoiceAsync(customer.Id, issue));
    }

    private static async Task<string> PdfTextAsync(TestTenant owner, InvoiceResponse invoice) =>
        PdfText.Of(await owner.Client.GetByteArrayAsync($"api/v1/invoices/{invoice.Id}/pdf"));
}
