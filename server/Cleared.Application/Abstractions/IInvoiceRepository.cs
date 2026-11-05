using Cleared.Domain.Invoicing;

namespace Cleared.Application.Abstractions;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken);

    // Newest first, by creation time and then id, so the order never depends on how rows are stored.
    Task<IReadOnlyList<Invoice>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
}
