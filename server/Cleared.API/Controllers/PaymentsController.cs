using Cleared.Application.Abstractions;
using Cleared.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/invoices/{invoiceId:guid}/payments")]
public sealed class PaymentsController(PaymentService paymentService, ITenantContext tenantContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PaymentResponse>> Record(
        Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await paymentService.RecordAsync(tenantContext.TenantId, invoiceId, request, cancellationToken);

        return payment is null
            ? NotFound()
            : Created($"/api/v1/invoices/{invoiceId}/payments/{payment.Id}", payment);
    }
}
