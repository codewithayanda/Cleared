using Microsoft.AspNetCore.Identity;

namespace Cleared.Infrastructure.Identity;

// A sign-in for an email nobody registered must cost what a wrong password costs, or the response
// time tells an attacker which emails have accounts. The decoy hash is made once with the same
// hasher, so verifying against it takes as long as verifying a real one.
public sealed class PasswordTimingEqualizer(IPasswordHasher<ApplicationUser> hasher)
{
    private static string? _decoyHash;

    public void Spend(string password)
    {
        _decoyHash ??= hasher.HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N"));

        hasher.VerifyHashedPassword(new ApplicationUser(), _decoyHash, password);
    }
}
