namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only counter, not a domain concept. Mirrors InvoiceNumberSequence but uses
// its own table so invoice and credit note numbering stay independent series with their own
// prefixes. Backs ICreditNoteNumberAllocator.
public sealed class CreditNoteNumberSequence
{
    public Guid TenantId { get; set; }

    public int Year { get; set; }

    public int NextValue { get; set; }
}
