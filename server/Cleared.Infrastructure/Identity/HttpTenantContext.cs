using Cleared.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Cleared.Infrastructure.Identity;

public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst("tenant_id")?.Value;

            if (claim is null || !Guid.TryParse(claim, out var tenantId))
            {
                throw new InvalidOperationException(
                    "No tenant_id claim on the current request. This should be unreachable behind [Authorize].");
            }

            return tenantId;
        }
    }
}
