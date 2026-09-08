using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// A plain parameter object, not an Entity — it exists only to carry one line's worth of
// data into CreditNote.Create without a long parallel-list parameter signature. The
// caller (CreditNoteService) resolves Description/UnitPrice/VatTreatment from the
// original InvoiceLineItem being credited; a client never gets to supply those directly,
// which is what stops a credit note being issued at a different price than was charged.
public sealed record CreditNoteLineRequest(
    Guid InvoiceLineItemId,
    string Description,
    decimal Quantity,
    Money UnitPrice,
    VatTreatment VatTreatment);
