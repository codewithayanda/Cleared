using Cleared.Domain.Invoicing;

namespace Cleared.Application.Abstractions;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    // Takes the invoice's lines with it. Only a draft is ever removed.
    void Remove(Invoice invoice);

    Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken);

    // Newest first, by creation time and then id, so the order never depends on how rows are stored.
    Task<IReadOnlyList<Invoice>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
}
