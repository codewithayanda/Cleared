using System.Globalization;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.Invoices;

internal static class InvoiceMapper
{
    // amountPaid is supplied by the caller (InvoiceService), which can see every Payment —
    // Invoice itself has no reference back to them (same reasoning as
    // Invoice.RecordPaymentTotal). A draft, or an invoice just issued in this same call,
    // can never have a payment against it yet, so callers on those paths pass Money.Zero
    // rather than querying for it.
    public static InvoiceResponse ToResponse(Invoice invoice, Money amountPaid) => new(
        invoice.Id,
        invoice.TenantId,
        invoice.CustomerId,
        invoice.Status.ToString(),
        invoice.DocumentType?.ToString(),
        invoice.Number,
        invoice.IssueDate,
        invoice.SupplyDate,
        invoice.DueDate,
        invoice.VatRateApplied?.ToString(CultureInfo.InvariantCulture),
        FormatMoney(invoice.Subtotal),
        FormatMoney(invoice.VatTotal),
        FormatMoney(invoice.Total),
        FormatMoney(amountPaid),
        FormatMoney(invoice.Total.Subtract(amountPaid)),
        invoice.Lines
            .Select(l => new InvoiceLineResponse(
                l.Id,
                l.Description,
                l.Quantity,
                FormatMoney(l.UnitPrice),
                FormatMoney(l.LineSubtotal),
                l.VatTreatment.ToString(),
                FormatMoney(l.LineVat),
                FormatMoney(l.LineTotal)))
            .ToList());

    private static string FormatMoney(Money money) => money.Amount.ToString("F2", CultureInfo.InvariantCulture);
}
