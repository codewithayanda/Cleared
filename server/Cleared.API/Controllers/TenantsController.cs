using Cleared.Application.Tenants;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Route("api/v1/tenants")]
public sealed class TenantsController(RegisterTenantService registerTenantService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TenantResponse>> Register(
        RegisterTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await registerTenantService.RegisterAsync(request, cancellationToken);

        return Created($"/api/v1/tenants/{tenant.Id}", tenant);
    }
}
