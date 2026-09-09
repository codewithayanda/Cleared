namespace Cleared.Application.Invoices;

// Money amounts are strings on the wire, never JSON numbers — JavaScript's float64
// can silently corrupt a decimal (see the API contract conventions).
public sealed record InvoiceLineResponse(
    Guid Id,
    string Description,
    decimal Quantity,
    string UnitPrice,
    string LineSubtotal,
    string VatTreatment,
    string LineVat,
    string LineTotal);

public sealed record InvoiceResponse(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    string Status,
    string? DocumentType,
    string? Number,
    DateOnly? IssueDate,
    DateOnly? SupplyDate,
    DateOnly? DueDate,
    string? VatRateApplied,
    string Subtotal,
    string VatTotal,
    string Total,
    string AmountPaid,
    string BalanceDue,
    IReadOnlyList<InvoiceLineResponse> Lines);
