using Cleared.API.Idempotency;
using Cleared.Application.Abstractions;
using Cleared.Application.Idempotency;
using Cleared.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/invoices")]
public sealed class InvoicesController(
    InvoiceService invoiceService, IdempotentExecutor idempotent, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InvoiceResponse>> Create(
        CreateInvoiceRequest request,
        [FromHeader(Name = IdempotencyHeader.Name), BindRequired] Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await idempotent.ExecuteAsync(
            tenantContext.TenantId, idempotencyKey, "CreateInvoice", request,
            () => invoiceService.CreateAsync(tenantContext.TenantId, request, cancellationToken),
            cancellationToken);
        Response.MarkReplayed(result);

        return Created($"/api/v1/invoices/{result.Value.Id}", result.Value);
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
        Guid id,
        IssueInvoiceRequest request,
        [FromHeader(Name = IdempotencyHeader.Name), BindRequired] Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await idempotent.ExecuteAsync(
            tenantContext.TenantId, idempotencyKey, "IssueInvoice", new { id, request },
            () => invoiceService.IssueAsync(tenantContext.TenantId, id, request, cancellationToken),
            cancellationToken);
        Response.MarkReplayed(result);

        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken cancellationToken)
    {
        var pdf = await invoiceService.GetPdfAsync(tenantContext.TenantId, id, cancellationToken);

        return pdf is null ? NotFound() : File(pdf, "application/pdf", $"invoice-{id}.pdf");
    }
}
