using System.Security.Claims;
using Cleared.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Cleared.Infrastructure.Identity;

// TokenService writes the user id to the standard "sub" claim, but ASP.NET Core's JWT
// handler remaps well-known claim types on the way in (unless MapInboundClaims is
// disabled, which Program.cs doesn't do) — "sub" arrives on the principal as
// ClaimTypes.NameIdentifier, not as "sub" itself. tenant_id is a custom claim name, so it
// isn't remapped, which is why HttpTenantContext can look it up directly.
public sealed class HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public Guid UserId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (claim is null || !Guid.TryParse(claim, out var userId))
            {
                throw new InvalidOperationException(
                    "No user id claim on the current request. This should be unreachable behind [Authorize].");
            }

            return userId;
        }
    }
}
