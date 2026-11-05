using System.Globalization;
using Cleared.Domain.Payments;

namespace Cleared.Application.Payments;

internal static class PaymentMapper
{
    public static PaymentResponse ToResponse(Payment payment) => new(
        payment.Id,
        payment.TenantId,
        payment.InvoiceId,
        payment.Method.ToString(),
        payment.Amount.Amount.ToString("F2", CultureInfo.InvariantCulture),
        payment.ReceivedAt,
        payment.RecordedAt,
        payment.Reference);
}
