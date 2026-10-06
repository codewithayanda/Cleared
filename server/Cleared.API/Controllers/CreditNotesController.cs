using Cleared.API.Idempotency;
using Cleared.Application.Abstractions;
using Cleared.Application.CreditNotes;
using Cleared.Application.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/invoices/{invoiceId:guid}/credit-notes")]
public sealed class CreditNotesController(
    CreditNoteService creditNoteService, IdempotentExecutor idempotent, ITenantContext tenantContext)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CreditNoteResponse>> Create(
        Guid invoiceId,
        CreateCreditNoteRequest request,
        [FromHeader(Name = IdempotencyHeader.Name), BindRequired] Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await idempotent.ExecuteAsync(
            tenantContext.TenantId, idempotencyKey, "CreateCreditNote", new { invoiceId, request },
            () => creditNoteService.CreateAsync(tenantContext.TenantId, invoiceId, request, cancellationToken),
            cancellationToken);
        Response.MarkReplayed(result);

        return result.Value is null
            ? NotFound()
            : Created($"/api/v1/invoices/{invoiceId}/credit-notes/{result.Value.Id}", result.Value);
    }
}
