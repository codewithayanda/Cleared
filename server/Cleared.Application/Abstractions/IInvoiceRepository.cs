using Cleared.Domain.Invoicing;

namespace Cleared.Application.Abstractions;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Invoice>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
}
