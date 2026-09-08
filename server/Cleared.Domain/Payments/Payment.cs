using Cleared.Domain.Common;

namespace Cleared.Domain.Payments;

public sealed class Payment : Entity
{
    public Guid TenantId { get; }
    public Guid InvoiceId { get; }
    public PaymentMethod Method { get; }

    // A free-text note the Owner types when recording what they saw arrive (e.g. a bank
    // reference) — for their own reconciliation, not matched against anything by the
    // system. There's no structured payment-reference/reconciliation engine yet; the
    // Owner looks at their own invoice list and records against the one they recognise.
    public string? Reference { get; }

    // Not constructor parameters — see the note on Tenant.CreatedAt / InvoiceLineItem.UnitPrice.
    public Money Amount { get; private set; }
    public DateOnly ReceivedAt { get; private set; }

    private Payment(Guid id, Guid tenantId, Guid invoiceId, PaymentMethod method, string? reference) : base(id)
    {
        TenantId = tenantId;
        InvoiceId = invoiceId;
        Method = method;
        Reference = reference;
    }

    public static Payment RecordManual(
        Guid id, Guid tenantId, Guid invoiceId, Money amount, DateOnly receivedAt, string? reference = null)
    {
        if (amount.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount.Amount, "A payment amount must be greater than zero.");
        }

        return new Payment(id, tenantId, invoiceId, PaymentMethod.Manual, reference)
        {
            Amount = amount,
            ReceivedAt = receivedAt,
        };
    }
}
