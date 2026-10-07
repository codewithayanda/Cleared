namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only, reached through SignInThrottle. The key is a hash of the email, so the table
// does not list who has an account, and an email nobody registered gets a row like any other.
public sealed class SignInThrottleRecord
{
    public byte[] EmailHash { get; set; } = [];

    public int Attempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
