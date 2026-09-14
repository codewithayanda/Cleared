using System.Globalization;
using Cleared.Application.Abstractions;
using Cleared.Domain.Auditing;
using Cleared.Domain.Common;
using Cleared.Domain.Payments;

namespace Cleared.Application.Payments;

public sealed class PaymentService(
    IInvoiceRepository invoiceRepository,
    IPaymentRepository paymentRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUserContext,
    IClock clock)
{
    public async Task<PaymentResponse?> RecordAsync(
        Guid tenantId, Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var amount = Money.Zar(decimal.Parse(request.Amount, CultureInfo.InvariantCulture));

        // The running total, including this payment: RecordPaymentTotal needs it to decide
        // between PartiallyPaid and Paid, and to reject an overpayment.
        var existingPayments = await paymentRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken);
        var totalPaid = existingPayments.Aggregate(amount, (sum, payment) => sum.Add(payment.Amount));

        invoice.RecordPaymentTotal(totalPaid);

        var payment = Payment.RecordManual(
            Guid.NewGuid(), tenantId, invoiceId, amount, request.ReceivedAt, clock.UtcNow, request.Reference);

        await paymentRepository.AddAsync(payment, cancellationToken);

        var auditEntry = AuditLog.Record(
            Guid.NewGuid(), tenantId, currentUserContext.UserId, "Invoice", invoiceId, "PaymentRecorded",
            clock.UtcNow, $"Recorded a manual payment of R{FormatMoney(amount)}. Invoice now {invoice.Status}.");
        await auditLogRepository.AddAsync(auditEntry, cancellationToken);

        // One SaveChangesAsync is atomic across the payment insert, the status update and
        // the audit entry, so no explicit transaction is needed for the write itself.
        // TODO: the read-then-write above is not serialised. Two concurrent payments can
        // both pass RecordPaymentTotal and overpay the invoice. Needs SELECT ... FOR UPDATE.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(payment);
    }

    public async Task<IReadOnlyList<PaymentResponse>?> ListAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var payments = await paymentRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken);

        // Most recent first, matching the audit log, so the invoice's history reads top-down.
        return payments.OrderByDescending(p => p.RecordedAt).Select(ToResponse).ToList();
    }

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.Id,
        payment.TenantId,
        payment.InvoiceId,
        payment.Method.ToString(),
        FormatMoney(payment.Amount),
        payment.ReceivedAt,
        payment.RecordedAt,
        payment.Reference);

    private static string FormatMoney(Money money) => money.Amount.ToString("F2", CultureInfo.InvariantCulture);
}
