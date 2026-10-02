using Cleared.Application.Abstractions;
using Cleared.Application.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tenants")]
public sealed class TenantsController(RegisterTenantService registerTenantService, ITenantContext tenantContext)
    : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<TenantResponse>> GetMine(CancellationToken cancellationToken)
    {
        var tenant = await registerTenantService.GetByIdAsync(tenantContext.TenantId, cancellationToken);

        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPut("me")]
    public async Task<ActionResult<TenantResponse>> UpdateMine(
        UpdateTenantProfileRequest request, CancellationToken cancellationToken)
    {
        var tenant = await registerTenantService.UpdateProfileAsync(tenantContext.TenantId, request, cancellationToken);

        return tenant is null ? NotFound() : Ok(tenant);
    }
}
