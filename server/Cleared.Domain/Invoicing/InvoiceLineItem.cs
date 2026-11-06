using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class InvoiceLineItem : Entity
{
    public Guid InvoiceId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public VatTreatment VatTreatment { get; }

    // Where the line sits on the invoice, counting up from zero in the order the person entered them. Gaps are
    // fine, and only the order counts. A line's id is a random Guid and the database gives no order of its own.
    public int Position { get; private set; }

    // Money is an owned type in the EF model (amount + currency columns), and EF Core's
    // constructor binding cannot compose a nested complex value into a constructor
    // parameter. Set via private setters in Create() and ApplyVatRate() instead.
    public Money UnitPrice { get; private set; }
    public Money LineSubtotal { get; private set; }

    // Zero until the parent Invoice is issued: a draft has no VAT rate yet.
    public Money LineVat { get; private set; } = Money.Zero;

    public Money LineTotal => LineSubtotal.Add(LineVat);

    private InvoiceLineItem(Guid id, Guid invoiceId, string description, decimal quantity, VatTreatment vatTreatment)
        : base(id)
    {
        InvoiceId = invoiceId;
        Description = description;
        Quantity = quantity;
        VatTreatment = vatTreatment;
    }

    internal static InvoiceLineItem Create(
        Guid id,
        Guid invoiceId,
        int position,
        string description,
        decimal quantity,
        Money unitPrice,
        VatTreatment vatTreatment)
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

        return new InvoiceLineItem(id, invoiceId, description, quantity, vatTreatment)
        {
            Position = position,
            UnitPrice = unitPrice,
            LineSubtotal = unitPrice.MultiplyBy(quantity),
        };
    }

    // This line's VAT at the given rate. Used both to apply a rate and to preview a total
    // before issue (see Invoice.ProspectiveTotal). ZeroRated, Exempt and NotApplicable
    // charge zero regardless of the rate in effect.
    public Money PreviewVat(decimal vatRate) =>
        VatTreatment == VatTreatment.Standard ? LineSubtotal.MultiplyBy(vatRate) : Money.Zero;

    // Called once, from Invoice.Issue(), with the rate effective on the invoice's supply date.
    internal void ApplyVatRate(decimal vatRate)
    {
        LineVat = PreviewVat(vatRate);
    }
}
