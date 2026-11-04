namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only, reached through EmailVerificationService. Only the hash of a link's secret is
// stored, so a copy of this table cannot confirm anyone's email.
public sealed class EmailVerificationTokenRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    // Set when the link was used or replaced by a newer one.
    public DateTimeOffset? ConsumedAt { get; set; }
}
