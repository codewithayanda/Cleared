namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only, like the number sequences: IdempotencyStore reaches it with raw SQL.
// Response stays null until the work it guards is done, and that never commits half-finished.
public sealed class IdempotencyRecord
{
    public Guid TenantId { get; set; }

    public Guid IdempotencyKey { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public string? Response { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
