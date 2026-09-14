using System.Security.Claims;
using Cleared.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Cleared.Infrastructure.Identity;

// TokenService writes the user id to the "sub" claim, but ASP.NET Core's JWT handler remaps
// well-known claim types on the way in, so it arrives as ClaimTypes.NameIdentifier.
// tenant_id is a custom name and is not remapped, so HttpTenantContext reads it directly.
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
