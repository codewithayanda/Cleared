using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// A parameter object, not an Entity: it carries one line's data into CreditNote.Create
// without a long parallel-list signature. CreditNoteService fills Description, UnitPrice
// and VatTreatment from the original line, so a client cannot credit at a different price.
public sealed record CreditNoteLineRequest(
    Guid InvoiceLineItemId,
    string Description,
    decimal Quantity,
    Money UnitPrice,
    VatTreatment VatTreatment);
