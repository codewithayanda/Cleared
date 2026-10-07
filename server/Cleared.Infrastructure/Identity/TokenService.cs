using System.Security.Claims;
using System.Text;
using Cleared.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cleared.Infrastructure.Identity;

// Access tokens live 15 minutes and cannot be revoked, so they carry only what a request needs.
// Sessions that outlive them are SessionService's job.
public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public string IssueAccessToken(Guid userId, Guid tenantId, string role)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via user-secrets before starting the API.");

        // Real time on purpose: the bearer handler checks these claims against the system clock.
        var now = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "Cleared",
            Audience = "Cleared",
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim(ClaimTypes.Role, role),
            ]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
