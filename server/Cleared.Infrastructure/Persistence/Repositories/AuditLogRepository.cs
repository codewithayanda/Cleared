using Cleared.Application.Abstractions;
using Cleared.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class AuditLogRepository(ClearedDbContext dbContext) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog entry, CancellationToken cancellationToken) =>
        await dbContext.AuditLogs.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<AuditLog>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.AuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(cancellationToken);
}
