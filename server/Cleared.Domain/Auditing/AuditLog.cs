using Cleared.Domain.Common;

namespace Cleared.Domain.Auditing;

// Append-only: nothing updates or deletes an entry, and nothing holds a reference to one.
// Details is a free-form summary ("Issued as INV-2026-0001, total R1150.00"), not a
// before/after diff: every audited action is a creation or state transition, not an edit.
public sealed class AuditLog : Entity
{
    public Guid TenantId { get; }
    public string EntityType { get; }
    public string Action { get; }
    public string? Details { get; }

    // Private setters, not constructor parameters: EF Core's constructor binding rejects
    // this constructor once it takes UserId and EntityId as well as id/tenantId.
    // See InvoiceLineItem.UnitPrice.
    public Guid UserId { get; private set; }
    public Guid EntityId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditLog(Guid id, Guid tenantId, string entityType, string action, string? details)
        : base(id)
    {
        TenantId = tenantId;
        EntityType = entityType;
        Action = action;
        Details = details;
    }

    public static AuditLog Record(
        Guid id,
        Guid tenantId,
        Guid userId,
        string entityType,
        Guid entityId,
        string action,
        DateTimeOffset occurredAt,
        string? details = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("An audit log entry must name the entity type it concerns.", nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("An audit log entry must describe the action taken.", nameof(action));
        }

        return new AuditLog(id, tenantId, entityType, action, details)
        {
            UserId = userId,
            EntityId = entityId,
            OccurredAt = occurredAt,
        };
    }
}
