using Cleared.Application.Customers;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(CreateCustomerService customerService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customerService.CreateAsync(request, cancellationToken);

        return Created($"/api/v1/customers/{customer.Id}", customer);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List(
        [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        return Ok(await customerService.ListAsync(tenantId, cancellationToken));
    }
}
