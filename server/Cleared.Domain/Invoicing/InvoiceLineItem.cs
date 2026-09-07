using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class InvoiceLineItem : Entity
{
    public Guid InvoiceId { get; }
    public string Description { get; }
    public decimal Quantity { get; }

    // Money is a complex/owned type in the EF Core model (two columns: amount + currency).
    // EF Core's constructor-binding materialization can only match constructor parameters
    // to plain scalar properties — it cannot compose a nested complex value and hand it into
    // the outer constructor. So these two are set via private setters, after construction,
    // inside Create() below — not passed as constructor arguments like everything else here.
    public Money UnitPrice { get; private set; }
    public Money LineSubtotal { get; private set; }

    private InvoiceLineItem(Guid id, Guid invoiceId, string description, decimal quantity)
        : base(id)
    {
        InvoiceId = invoiceId;
        Description = description;
        Quantity = quantity;
    }

    internal static InvoiceLineItem Create(Guid id, Guid invoiceId, string description, decimal quantity, Money unitPrice)
    {
        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("A line item must belong to an invoice.", nameof(invoiceId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A line item must have a description.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be greater than zero.");
        }

        return new InvoiceLineItem(id, invoiceId, description, quantity)
        {
            UnitPrice = unitPrice,
            LineSubtotal = unitPrice.MultiplyBy(quantity),
        };
    }
}
