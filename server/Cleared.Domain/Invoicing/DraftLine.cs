using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// One line as it is sent to change a draft, before it has become a line of the invoice.
public sealed record DraftLine(
    Guid Id, string Description, decimal Quantity, Money UnitPrice, VatTreatment VatTreatment);
