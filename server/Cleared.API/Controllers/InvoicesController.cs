using Cleared.Application.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

// tenantId travels via query string for now — there is no auth/tenant-context yet.
// Replace with a signed token claim before this is exposed beyond local development.
[ApiController]
[Route("api/v1/invoices")]
public sealed class InvoicesController(InvoiceService invoiceService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InvoiceResponse>> Create(
        CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.CreateAsync(request, cancellationToken);

        return Created($"/api/v1/invoices/{invoice.Id}", invoice);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceResponse>> GetById(
        Guid id, [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.GetByIdAsync(tenantId, id, cancellationToken);

        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceResponse>>> List(
        [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        return Ok(await invoiceService.ListAsync(tenantId, cancellationToken));
    }

    [HttpPost("{id:guid}/issue")]
    public async Task<ActionResult<InvoiceResponse>> Issue(
        Guid id, [FromQuery] Guid tenantId, IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceService.IssueAsync(tenantId, id, request, cancellationToken);

        return invoice is null ? NotFound() : Ok(invoice);
    }
}
