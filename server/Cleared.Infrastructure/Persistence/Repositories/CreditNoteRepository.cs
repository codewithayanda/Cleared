using Cleared.Application.Abstractions;
using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class CreditNoteRepository(ClearedDbContext dbContext) : ICreditNoteRepository
{
    public async Task AddAsync(CreditNote creditNote, CancellationToken cancellationToken) =>
        await dbContext.CreditNotes.AddAsync(creditNote, cancellationToken);

    public async Task<IReadOnlyList<CreditNote>> ListByInvoiceIdAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        await dbContext.CreditNotes
            .Include(cn => cn.Lines)
            .Where(cn => cn.TenantId == tenantId && cn.InvoiceId == invoiceId)
            .ToListAsync(cancellationToken);
}
