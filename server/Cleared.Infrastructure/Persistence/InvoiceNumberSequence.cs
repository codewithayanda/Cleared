namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only counter, not a domain concept — it has no business rules of its own,
// it just backs IInvoiceNumberAllocator's atomic claim-and-increment.
public sealed class InvoiceNumberSequence
{
    public Guid TenantId { get; set; }

    public int Year { get; set; }

    public int NextValue { get; set; }
}
