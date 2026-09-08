using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class InvoiceLineItem : Entity
{
    public Guid InvoiceId { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public VatTreatment VatTreatment { get; }

    // Money is a complex/owned type in the EF Core model (two columns: amount + currency).
    // EF Core's constructor-binding materialization can only match constructor parameters
    // to plain scalar properties — it cannot compose a nested complex value and hand it into
    // the outer constructor. So these are set via private setters, after construction,
    // inside Create()/ApplyVatRate() below — not passed as constructor arguments.
    public Money UnitPrice { get; private set; }
    public Money LineSubtotal { get; private set; }

    // Zero until the parent Invoice is issued — a draft invoice has no VAT rate yet.
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
        Guid id, Guid invoiceId, string description, decimal quantity, Money unitPrice, VatTreatment vatTreatment)
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
            UnitPrice = unitPrice,
            LineSubtotal = unitPrice.MultiplyBy(quantity),
        };
    }

    // What this line's VAT would be at the given rate — used both to actually apply a
    // rate (below) and to preview a total before an invoice is issued (see
    // Invoice.ProspectiveTotal), without duplicating the treatment-based rule in two
    // places. ZeroRated, Exempt and NotApplicable lines always charge zero VAT regardless
    // of the rate in effect — that distinction is the entire reason those treatments exist.
    public Money PreviewVat(decimal vatRate) =>
        VatTreatment == VatTreatment.Standard ? LineSubtotal.MultiplyBy(vatRate) : Money.Zero;

    // Called once, from Invoice.Issue(), with the rate effective on the invoice's supply date.
    internal void ApplyVatRate(decimal vatRate)
    {
        LineVat = PreviewVat(vatRate);
    }
}
