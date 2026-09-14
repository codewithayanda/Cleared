namespace Cleared.Domain.Invoicing;

// Thrown when a document about to be issued as a TaxInvoice (see DocumentType) is missing
// a field the VAT Act s20(4) requires. Blocking the issue is the last free correction: an
// issued invoice is immutable and needs a credit note.
public sealed class TaxInvoiceValidationException(IReadOnlyList<string> missingFields)
    : Exception($"This tax invoice is missing required fields: {string.Join(", ", missingFields)}")
{
    public IReadOnlyList<string> MissingFields { get; } = missingFields;
}
