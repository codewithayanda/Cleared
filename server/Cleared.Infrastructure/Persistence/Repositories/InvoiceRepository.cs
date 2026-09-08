using Cleared.Application.Abstractions;
using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class InvoiceRepository(ClearedDbContext dbContext) : IInvoiceRepository
{
    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken) =>
        await dbContext.Invoices.AddAsync(invoice, cancellationToken);

    public async Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invoiceId, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .Include(i => i.Lines)
            .Where(i => i.TenantId == tenantId)
            .ToListAsync(cancellationToken);
}
