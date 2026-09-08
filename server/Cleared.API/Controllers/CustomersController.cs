using Cleared.Application.Abstractions;
using Cleared.Application.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/customers")]
public sealed class CustomersController(CreateCustomerService customerService, ITenantContext tenantContext)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customerService.CreateAsync(tenantContext.TenantId, request, cancellationToken);

        return Created($"/api/v1/customers/{customer.Id}", customer);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await customerService.ListAsync(tenantContext.TenantId, cancellationToken));
    }
}
