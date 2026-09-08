namespace Cleared.Application.Abstractions;

public interface IInvoiceNumberAllocator
{
    Task<string> AllocateAsync(Guid tenantId, int year, CancellationToken cancellationToken);
}
