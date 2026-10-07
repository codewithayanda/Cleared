using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Cleared.Integration.Tests;

// Counts password checks, so a test can say how many guesses were really verified.
public sealed class CountingPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private readonly PasswordHasher<ApplicationUser> _inner = new();
    private int _verifications;

    public int Verifications => Volatile.Read(ref _verifications);

    public string HashPassword(ApplicationUser user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(
        ApplicationUser user, string hashedPassword, string providedPassword)
    {
        Interlocked.Increment(ref _verifications);

        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
