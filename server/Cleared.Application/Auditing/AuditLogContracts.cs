namespace Cleared.Application.Auditing;

public sealed record AuditLogResponse(
    Guid Id,
    Guid UserId,
    string EntityType,
    Guid EntityId,
    string Action,
    string? Details,
    DateTimeOffset OccurredAt);
