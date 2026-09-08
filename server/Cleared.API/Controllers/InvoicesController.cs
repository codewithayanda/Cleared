using Cleared.Application.Abstractions;
using Cleared.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/invoices")]
public sealed class InvoicesController(InvoiceService invoiceService, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InvoiceResponse>> Create(
        CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.CreateAsync(tenantContext.TenantId, request, cancellationToken);

        return Created($"/api/v1/invoices/{invoice.Id}", invoice);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.GetByIdAsync(tenantContext.TenantId, id, cancellationToken);

        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceResponse>>> List(CancellationToken cancellationToken)
    {
        return Ok(await invoiceService.ListAsync(tenantContext.TenantId, cancellationToken));
    }

    [HttpPost("{id:guid}/issue")]
    public async Task<ActionResult<InvoiceResponse>> Issue(
        Guid id, IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.IssueAsync(tenantContext.TenantId, id, request, cancellationToken);

        return invoice is null ? NotFound() : Ok(invoice);
    }
}
