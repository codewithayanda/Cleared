namespace Cleared.Application.Abstractions;

// Takes a row lock on one invoice, held until the surrounding transaction ends, so concurrent
// writers to that invoice queue instead of acting on the same stale read. Call it inside a
// transaction and before reading the invoice, so the read sees what the last holder committed.
// Returns false when the tenant has no such invoice.
public interface IInvoiceLock
{
    Task<bool> AcquireAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken);
}
