using System.Security.Cryptography;
using System.Text;

namespace Cleared.Infrastructure.Identity;

internal static class EmailKey
{
    // Capitals and spaces must not make a new key, so one email always counts as one. Only the hash is
    // stored, so a table keyed by it does not list who has an account.
    public static byte[] Hash(string email) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant()));
}
