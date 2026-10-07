using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Cleared.Infrastructure.Identity;

internal static class RefreshTokenSecret
{
    private const int SecretBytes = 32;

    // 256 random bits from the operating system, written in URL-safe base64 (43 characters).
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    // Anything that is not exactly the shape Generate produces is turned away before it can reach
    // the database. The token is random and long, so a plain SHA-256 is enough to store it safely.
    public static bool TryHash(string? token, out byte[] hash)
    {
        hash = [];

        if (token is null || !Base64Url.IsValid(token, out var decodedLength) || decodedLength != SecretBytes)
        {
            return false;
        }

        hash = SHA256.HashData(Encoding.ASCII.GetBytes(token));

        return true;
    }
}
