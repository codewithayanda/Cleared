using System.Globalization;
using Cleared.Application.Customers;
using Cleared.Application.Payments;
using Cleared.Application.Tenants;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.Invoices;

// Dates and numbers are written in the invariant culture, so an invoice reads the same on any server.
// "9 Oct 2026" cannot be taken for the 10th of September, as 10/9/2026 can.
public static class InvoiceDocumentBuilder
{
    private const string _dateFormat = "d MMM yyyy";

    // asAt is the day the document is made, which payments are listed up to.
    public static InvoiceDocument Build(
        InvoiceResponse invoice,
        CustomerResponse customer,
        TenantResponse tenant,
        IReadOnlyList<PaymentResponse> payments,
        DateOnly asAt)
    {
        var showVat = invoice.DocumentType == nameof(DocumentType.TaxInvoice);
        var paid = invoice.Status == nameof(InvoiceStatus.Paid);
        var cancelled = invoice.Status == nameof(InvoiceStatus.Cancelled);
        var partPaid = invoice.Status == nameof(InvoiceStatus.PartiallyPaid);
        var (totalLabel, totalAmount) = TotalBox(invoice, paid, cancelled, partPaid);

        return new InvoiceDocument(
            showVat ? "TAX INVOICE" : "INVOICE",
            invoice.Number,
            paid ? "PAID" : cancelled ? "CANCELLED" : null,
            showVat,
            invoice.IssueDate is { } issued ? Format(issued) : null,
            invoice.DueDate is { } due ? Format(due) : null,
            showVat && invoice.VatRateApplied is { } rate ? Percent(rate) : null,
            [.. invoice.Lines.Select(Line)],
            invoice.Subtotal,
            invoice.VatTotal,
            // The box below carries the total only while nothing has been paid, so otherwise it says so here.
            paid || cancelled || partPaid ? invoice.Total : null,
            payments.Count > 0 ? $"Payments received as at {Format(asAt)}" : null,
            [.. payments
                .OrderBy(payment => payment.ReceivedAt)
                .ThenBy(payment => payment.RecordedAt)
                .Select(payment => new InvoiceDocumentPayment(Format(payment.ReceivedAt), payment.Amount))],
            totalLabel,
            totalAmount,
            // Nothing is left to pay on a paid or cancelled invoice, so where to pay would only mislead.
            !paid && !cancelled && HasBankDetails(tenant),
            customer,
            tenant);
    }

    // The dark box shows what is left to pay, so it never prints the original total once any is paid.
    private static (string Label, string? Amount) TotalBox(
        InvoiceResponse invoice, bool paid, bool cancelled, bool partPaid) =>
        cancelled ? ("Cancelled", null)
        : paid ? ("Paid in full", null)
        : partPaid ? ("Balance due", invoice.BalanceDue)
        : ("Total due", invoice.Total);

    private static InvoiceDocumentLine Line(InvoiceLineResponse line) => new(
        line.Description,
        line.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
        line.UnitPrice,
        line.VatTreatment,
        line.LineVat,
        line.LineTotal);

    private static string Format(DateOnly date) => date.ToString(_dateFormat, CultureInfo.InvariantCulture);

    private static string Percent(string rate) =>
        (decimal.Parse(rate, CultureInfo.InvariantCulture) * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";

    private static bool HasBankDetails(TenantResponse tenant) =>
        tenant.BankName is not null || tenant.BankAccountNumber is not null || tenant.BankBranchCode is not null;
}
