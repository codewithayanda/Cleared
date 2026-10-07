namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only, reached through SessionService. Only the hash of a token is stored, so a
// copy of this table cannot be used to sign in. A family is every token descended from one sign-in.
public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    // The user's security stamp when the session began. Identity changes it on a password change.
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    // Idle expiry of this token, never later than FamilyExpiresAt.
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset FamilyExpiresAt { get; set; }

    // Set when the token was swapped for ReplacedById. A used token that shows up again is a copy.
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? ReplacedById { get; set; }
}
