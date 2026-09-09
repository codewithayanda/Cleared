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

    // One query for every payment the tenant has ever recorded, grouped by invoice in
    // memory by the caller — cheaper and safer than summing Money (an EF complex type)
    // via a translated SQL aggregate, and avoids an N+1 per-invoice query when listing.
    public async Task<IReadOnlyList<Payment>> ListByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Payments
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);
}
