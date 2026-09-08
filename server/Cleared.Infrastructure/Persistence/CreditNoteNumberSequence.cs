namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only counter, not a domain concept — mirrors InvoiceNumberSequence but
// kept as a separate table so invoice and credit note numbering are independent series
// (D-K: separate sequence, separate prefix), backing ICreditNoteNumberAllocator.
public sealed class CreditNoteNumberSequence
{
    public Guid TenantId { get; set; }

    public int Year { get; set; }

    public int NextValue { get; set; }
}
