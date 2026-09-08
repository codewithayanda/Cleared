namespace Cleared.Application.CreditNotes;

// The client sends only which original line to credit and how much of it — never a price
// or VAT treatment. CreditNoteService resolves those from the original InvoiceLineItem,
// which is what stops a credit note being issued at a different price than was charged.
public sealed record CreateCreditNoteLineRequest(Guid InvoiceLineItemId, decimal Quantity);

public sealed record CreateCreditNoteRequest(string Reason, IReadOnlyList<CreateCreditNoteLineRequest> Lines);

public sealed record CreditNoteLineResponse(
    Guid Id,
    Guid InvoiceLineItemId,
    string Description,
    decimal Quantity,
    string UnitPrice,
    string LineSubtotal,
    string VatTreatment,
    string LineVat,
    string LineTotal);

public sealed record CreditNoteResponse(
    Guid Id,
    Guid TenantId,
    Guid InvoiceId,
    string Number,
    string Reason,
    DateOnly IssueDate,
    string Subtotal,
    string VatTotal,
    string Total,
    IReadOnlyList<CreditNoteLineResponse> Lines);
