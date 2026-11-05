using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;
using Cleared.Infrastructure.Documents;
using QuestPDF.Infrastructure;

namespace Cleared.Integration.Tests;

// The layout only draws what InvoiceDocumentBuilder decided. So these check that each part of a document
// is on the page when it is given and left off when it is not, reading the words back out of the PDF.
public class QuestPdfInvoiceRendererTests
{
    private static readonly CustomerResponse _customer =
        new(Guid.NewGuid(), Guid.NewGuid(), "Beta Co", "4987654321", null, "1 Main Road");

    private static readonly TenantResponse _tenant = new(
        Guid.NewGuid(), "Acme Ltd", "Acme Trading", "Registered", "4123456789", "1 Acme Way",
        "FNB", "62123456789", "250655");

    public QuestPdfInvoiceRendererTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static InvoiceDocument Document(
        bool showVat = true,
        string? mark = null,
        bool showBanking = true,
        string totalLabel = "Total due",
        string? totalAmount = "705.00",
        string? invoiceTotal = null,
        string? paymentsHeading = null,
        InvoiceDocumentPayment[]? payments = null) => new(
        showVat ? "TAX INVOICE" : "INVOICE", "INV-2026-0007", mark, showVat, "9 Oct 2026", "8 Nov 2026",
        showVat ? "15%" : null,
        [new InvoiceDocumentLine("Consulting", "1.5", "408.69", "Standard", "91.96", "705.00")],
        "613.04", "91.96", invoiceTotal, paymentsHeading, payments ?? [], totalLabel, totalAmount, showBanking,
        showVat ? _customer : _customer with { VatNumber = null },
        showVat ? _tenant : _tenant with { VatStatus = "NotRegistered", VatNumber = null });

    private static string TextOf(InvoiceDocument document) => PdfText.Of(new QuestPdfInvoiceRenderer().Render(document));

    [Fact]
    public void A_tax_invoice_shows_the_vat_columns_the_vat_row_and_the_rate()
    {
        var text = TextOf(Document(showVat: true));

        Assert.Contains("TAX INVOICE", text);
        Assert.Contains("VAT amount", text);
        Assert.Contains("VAT rate 15%", text);
        Assert.Contains("VAT R 91.96", text);
    }

    [Fact]
    public void An_invoice_with_no_vat_says_nothing_about_vat_anywhere()
    {
        var text = TextOf(Document(showVat: false));

        Assert.Contains("INVOICE", text);
        Assert.DoesNotContain("VAT", text);
    }

    [Fact]
    public void An_unpaid_invoice_shows_the_total_due_and_how_to_pay()
    {
        var text = TextOf(Document());

        Assert.Contains("Total due R 705.00", text);
        Assert.Contains("BANKING DETAILS FNB", text);
        Assert.Contains("Account number: 62123456789", text);
        Assert.Contains("Branch code: 250655", text);
        Assert.Contains("Payment reference: INV-2026-0007", text);
        Assert.DoesNotContain("Invoice total", text);
        Assert.DoesNotContain("Payments received", text);
    }

    [Fact]
    public void A_part_paid_invoice_shows_its_total_the_payments_and_the_balance()
    {
        var text = TextOf(Document(
            totalLabel: "Balance due",
            totalAmount: "305.00",
            invoiceTotal: "705.00",
            paymentsHeading: "Payments received as at 4 Nov 2026",
            payments: [new("12 Oct 2026", "200.00"), new("20 Oct 2026", "200.00")]));

        Assert.Contains("Invoice total R 705.00", text);
        Assert.Contains("Payments received as at 4 Nov 2026", text);
        Assert.Contains("Balance due R 305.00", text);
        var older = text.IndexOf("12 Oct 2026", StringComparison.Ordinal);
        var newer = text.IndexOf("20 Oct 2026", StringComparison.Ordinal);
        Assert.True(older >= 0 && older < newer, text);
        Assert.Contains("BANKING DETAILS", text);
        Assert.DoesNotContain("Total due", text);
    }

    [Fact]
    public void A_paid_invoice_shows_the_mark_and_no_amount_still_to_pay()
    {
        var text = TextOf(Document(
            mark: "PAID",
            showBanking: false,
            totalLabel: "Paid in full",
            totalAmount: null,
            invoiceTotal: "705.00",
            paymentsHeading: "Payments received as at 4 Nov 2026",
            payments: [new("20 Oct 2026", "705.00")]));

        Assert.Contains("PAID", text);
        Assert.Contains("Paid in full", text);
        Assert.DoesNotContain("Paid in full R", text);
        Assert.DoesNotContain("Total due", text);
        Assert.DoesNotContain("Balance due", text);
        Assert.DoesNotContain("BANKING DETAILS", text);
    }

    [Fact]
    public void A_cancelled_invoice_shows_the_mark_and_no_amount_still_to_pay()
    {
        var text = TextOf(Document(
            mark: "CANCELLED",
            showBanking: false,
            totalLabel: "Cancelled",
            totalAmount: null,
            invoiceTotal: "705.00"));

        Assert.Contains("CANCELLED", text);
        Assert.DoesNotContain("Cancelled R", text);
        Assert.DoesNotContain("Total due", text);
        Assert.DoesNotContain("BANKING DETAILS", text);
    }

    [Fact]
    public void A_draft_with_no_number_or_dates_still_draws_without_them()
    {
        var draft = Document() with { Number = null, IssueDate = null, DueDate = null, VatRate = null };

        var text = TextOf(draft);

        Assert.Contains("Total due", text);
        Assert.DoesNotContain("Issue date", text);
        Assert.DoesNotContain("Due date", text);
        Assert.DoesNotContain("Payment reference", text);
    }
}
