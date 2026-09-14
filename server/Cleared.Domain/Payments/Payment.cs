using Cleared.Domain.Common;

namespace Cleared.Domain.Payments;

public sealed class Payment : Entity
{
    public Guid TenantId { get; }
    public Guid InvoiceId { get; }
    public PaymentMethod Method { get; }

    // A free-text note the Owner types when recording what arrived (e.g. a bank reference),
    // for their own reconciliation. Nothing in the system matches against it.
    public string? Reference { get; }

    // Private setters, not constructor parameters. See InvoiceLineItem.UnitPrice.
    public Money Amount { get; private set; }
    public DateOnly ReceivedAt { get; private set; }

    // When this record was captured in Cleared, distinct from ReceivedAt, which is the date
    // the Owner says the money arrived and is often entered days later off a bank statement.
    // RecordedAt has a time component because it is a system timestamp, not a business fact.
    public DateTimeOffset RecordedAt { get; private set; }

    private Payment(Guid id, Guid tenantId, Guid invoiceId, PaymentMethod method, string? reference) : base(id)
    {
        TenantId = tenantId;
        InvoiceId = invoiceId;
        Method = method;
        Reference = reference;
    }

    public static Payment RecordManual(
        Guid id, Guid tenantId, Guid invoiceId, Money amount, DateOnly receivedAt, DateTimeOffset recordedAt,
        string? reference = null)
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
            RecordedAt = recordedAt,
        };
    }
}
