using Cleared.Application.Abstractions;
using Cleared.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository(ClearedDbContext dbContext) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken cancellationToken) =>
        await dbContext.Payments.AddAsync(payment, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListByInvoiceIdAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        await dbContext.Payments
            .Where(p => p.TenantId == tenantId && p.InvoiceId == invoiceId)
            .ToListAsync(cancellationToken);
}
