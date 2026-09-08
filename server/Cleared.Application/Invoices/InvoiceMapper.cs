using System.Globalization;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.Invoices;

internal static class InvoiceMapper
{
    public static InvoiceResponse ToResponse(Invoice invoice) => new(
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
