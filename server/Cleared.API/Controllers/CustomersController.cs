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
    CustomerService customerService, IdempotentExecutor idempotent, ITenantContext tenantContext)
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var customer = await customerService.GetAsync(tenantContext.TenantId, id, cancellationToken);

        return customer is null ? NotFound() : Ok(customer);
    }

    // A PUT, so doing it twice has the effect of doing it once and it needs no Idempotency-Key.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customerService.UpdateAsync(tenantContext.TenantId, id, request, cancellationToken);

        return customer is null ? NotFound() : Ok(customer);
    }
}
