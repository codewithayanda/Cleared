using Cleared.API.Idempotency;
using Cleared.Application.Abstractions;
using Cleared.Application.Customers;
using Cleared.Application.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/customers")]
public sealed class CustomersController(
    CreateCustomerService customerService, IdempotentExecutor idempotent, ITenantContext tenantContext)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        CreateCustomerRequest request,
        [FromHeader(Name = IdempotencyHeader.Name), BindRequired] Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await idempotent.ExecuteAsync(
            tenantContext.TenantId, idempotencyKey, "CreateCustomer", request,
            () => customerService.CreateAsync(tenantContext.TenantId, request, cancellationToken),
            cancellationToken);
        Response.MarkReplayed(result);

        return Created($"/api/v1/customers/{result.Value.Id}", result.Value);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await customerService.ListAsync(tenantContext.TenantId, cancellationToken));
    }
}
