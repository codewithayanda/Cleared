using Cleared.Application.Abstractions;
using Cleared.Domain.Auditing;

namespace Cleared.Application.Auditing;

// Read-only from here — every write goes through IAuditLogRepository directly from
// whichever service performed the audited action (InvoiceService, CreditNoteService,
// PaymentService), since an audit entry is a side effect of that action, not a use case
// of its own.
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
