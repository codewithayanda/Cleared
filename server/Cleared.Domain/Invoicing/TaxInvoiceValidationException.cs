namespace Cleared.Domain.Invoicing;

// Raised when a document is about to be issued as a TaxInvoice (see DocumentType) but is
// missing a field the VAT Act (s20(4)) requires on one. Better to block the issue than to
// hand a customer a legally invalid tax invoice — an issued invoice is immutable, so this
// is the last point a correction is free; afterwards it needs a credit note.
public sealed class TaxInvoiceValidationException(IReadOnlyList<string> missingFields)
    : Exception($"This tax invoice is missing required fields: {string.Join(", ", missingFields)}")
{
    public IReadOnlyList<string> MissingFields { get; } = missingFields;
}
