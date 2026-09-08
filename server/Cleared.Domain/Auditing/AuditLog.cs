using Cleared.Domain.Common;

namespace Cleared.Domain.Auditing;

// Append-only by design: nothing may update or delete an entry, and nothing else in the
// domain holds a reference back to one. Details is a free-form summary of what happened
// (e.g. "Issued as INV-2026-0001, total R1150.00"), not a structured before/after diff —
// every action audited so far (issue, credit, payment) is a one-way creation or state
// transition, not a field-level edit, so there's no meaningful "before" to diff against.
public sealed class AuditLog : Entity
{
    public Guid TenantId { get; }
    public string EntityType { get; }
    public string Action { get; }
    public string? Details { get; }

    // Not constructor parameters — see the note on Tenant.CreatedAt / InvoiceLineItem.UnitPrice.
    // EF Core's constructor-binding rejected this whole constructor once it had two Guid
    // parameters beyond id/tenantId (UserId, EntityId) — same limitation, a different shape.
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
