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

    // Every payment the tenant has recorded, grouped by invoice in memory by the caller.
    // Summing Money in SQL is awkward because it is an EF complex type.
    // TODO: unbounded, and not paginated. See the TODO in InvoiceService.ListAsync.
    public async Task<IReadOnlyList<Payment>> ListByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Payments
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);
}
