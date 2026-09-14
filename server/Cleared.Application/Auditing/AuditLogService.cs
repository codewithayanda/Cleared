using Cleared.Application.Abstractions;
using Cleared.Domain.Auditing;

namespace Cleared.Application.Auditing;

// Read-only. Writes go through IAuditLogRepository from whichever service performed the
// action (InvoiceService, CreditNoteService, PaymentService), since an audit entry is a
// side effect of that action rather than a use case of its own.
public sealed class AuditLogService(IAuditLogRepository auditLogRepository)
{
    public async Task<IReadOnlyList<AuditLogResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await auditLogRepository.ListAsync(tenantId, cancellationToken);

        return entries
            .OrderByDescending(entry => entry.OccurredAt)
            .Select(ToResponse)
            .ToList();
    }

    private static AuditLogResponse ToResponse(AuditLog entry) => new(
        entry.Id, entry.UserId, entry.EntityType, entry.EntityId, entry.Action, entry.Details, entry.OccurredAt);
}
