namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only, reached through PasswordResetService. Only the hash of a link's secret is stored,
// so a copy of this table cannot be used to reset anyone's password.
public sealed class PasswordResetTokenRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    // Set when the link was used, replaced by a newer one, or ended by a password change.
    public DateTimeOffset? ConsumedAt { get; set; }
}
