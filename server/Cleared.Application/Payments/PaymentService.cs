using System.Globalization;
using Cleared.Application.Abstractions;
using Cleared.Application.Common;
using Cleared.Domain.Auditing;
using Cleared.Domain.Common;
using Cleared.Domain.Payments;

namespace Cleared.Application.Payments;

public sealed class PaymentService(
    IInvoiceRepository invoiceRepository,
    IInvoiceLock invoiceLock,
    IPaymentRepository paymentRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUserContext,
    IClock clock)
{
    public async Task<PaymentResponse?> RecordAsync(
        Guid tenantId, Guid invoiceId, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        // The invoice row lock serialises concurrent payments, so each one sees the total the
        // last one committed. See InvoiceConcurrencyTests.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var invoice = await invoiceLock.AcquireAsync(tenantId, invoiceId, cancellationToken)
            ? await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken)
            : null;
        if (invoice is null)
        {
            return null;
        }

        var amount = Money.Zar(MoneyText.Parse(request.Amount, "Amount"));

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

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PaymentMapper.ToResponse(payment);
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
        return payments.OrderByDescending(p => p.RecordedAt).Select(PaymentMapper.ToResponse).ToList();
    }

    private static string FormatMoney(Money money) => money.Amount.ToString("F2", CultureInfo.InvariantCulture);
}
