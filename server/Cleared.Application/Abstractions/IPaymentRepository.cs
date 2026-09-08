using Cleared.Domain.Payments;

namespace Cleared.Application.Abstractions;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);

    Task<IReadOnlyList<Payment>> ListByInvoiceIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken);
}
