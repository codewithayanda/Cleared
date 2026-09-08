using Cleared.Application.Abstractions;
using Cleared.Application.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/audit")]
public sealed class AuditController(AuditLogService auditLogService, ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditLogResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await auditLogService.ListAsync(tenantContext.TenantId, cancellationToken));
    }
}
