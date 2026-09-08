using Cleared.Domain.Auditing;

namespace Cleared.Application.Abstractions;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditLog>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
}
