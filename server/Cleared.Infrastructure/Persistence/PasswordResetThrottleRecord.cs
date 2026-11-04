namespace Cleared.Infrastructure.Persistence;

// Infrastructure-only, reached through PasswordResetService. The key is a hash of the email, so an email
// nobody registered is counted like any other and the table does not list who has an account.
public sealed class PasswordResetThrottleRecord
{
    public byte[] EmailHash { get; set; } = [];

    public DateTimeOffset WindowStart { get; set; }

    public int Requests { get; set; }
}
