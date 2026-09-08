using Cleared.Application.Abstractions;
using Cleared.Application.CreditNotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/invoices/{invoiceId:guid}/credit-notes")]
public sealed class CreditNotesController(CreditNoteService creditNoteService, ITenantContext tenantContext)
    : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CreditNoteResponse>> Create(
        Guid invoiceId, CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var creditNote = await creditNoteService.CreateAsync(
            tenantContext.TenantId, invoiceId, request, cancellationToken);

        return creditNote is null
            ? NotFound()
            : Created($"/api/v1/invoices/{invoiceId}/credit-notes/{creditNote.Id}", creditNote);
    }
}
