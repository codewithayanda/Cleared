using Cleared.Application.Abstractions;
using Cleared.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository(ClearedDbContext dbContext) : ITenantRepository
{
    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken) =>
        await dbContext.Tenants.AddAsync(tenant, cancellationToken);

    public async Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
}
