using Cleared.API.Idempotency;
using Cleared.Application.Abstractions;
using Cleared.Application.Idempotency;
using Cleared.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/invoices/{invoiceId:guid}/payments")]
public sealed class PaymentsController(
    PaymentService paymentService, IdempotentExecutor idempotent, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> Record(
        Guid invoiceId,
        RecordPaymentRequest request,
        [FromHeader(Name = IdempotencyHeader.Name), BindRequired] Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await idempotent.ExecuteAsync(
            tenantContext.TenantId, idempotencyKey, "RecordPayment", new { invoiceId, request },
            () => paymentService.RecordAsync(tenantContext.TenantId, invoiceId, request, cancellationToken),
            cancellationToken);
        Response.MarkReplayed(result);

        return result.Value is null
            ? NotFound()
            : Created($"/api/v1/invoices/{invoiceId}/payments/{result.Value.Id}", result.Value);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentResponse>>> List(
        Guid invoiceId, CancellationToken cancellationToken)
    {
        var payments = await paymentService.ListAsync(tenantContext.TenantId, invoiceId, cancellationToken);

        return payments is null ? NotFound() : Ok(payments);
    }
}
