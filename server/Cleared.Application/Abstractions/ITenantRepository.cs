using Cleared.Domain.Tenancy;

namespace Cleared.Application.Abstractions;

public interface ITenantRepository
{
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken);
}
