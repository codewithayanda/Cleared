using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class CreditNoteLineItem : Entity
{
    public Guid CreditNoteId { get; }

    // The original invoice line this credits. A credit note line always corrects
    // something that was actually charged.
    public Guid InvoiceLineItemId { get; }

    public string Description { get; }
    public decimal Quantity { get; }
    public VatTreatment VatTreatment { get; }

    public Money UnitPrice { get; private set; }
    public Money LineSubtotal { get; private set; }
    public Money LineVat { get; private set; }

    public Money LineTotal => LineSubtotal.Add(LineVat);

    private CreditNoteLineItem(
        Guid id, Guid creditNoteId, Guid invoiceLineItemId, string description, decimal quantity, VatTreatment vatTreatment)
        : base(id)
    {
        CreditNoteId = creditNoteId;
        InvoiceLineItemId = invoiceLineItemId;
        Description = description;
        Quantity = quantity;
        VatTreatment = vatTreatment;
    }

    // vatRate is the original invoice's VatRateApplied, not today's rate. A credit issued
    // after a rate change must use the rate that invoice charged, or it stops balancing.
    internal static CreditNoteLineItem Create(
        Guid id,
        Guid creditNoteId,
        Guid invoiceLineItemId,
        string description,
        decimal quantity,
        Money unitPrice,
        VatTreatment vatTreatment,
        decimal vatRate)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be greater than zero.");
        }

        var lineSubtotal = unitPrice.MultiplyBy(quantity);

        return new CreditNoteLineItem(id, creditNoteId, invoiceLineItemId, description, quantity, vatTreatment)
        {
            UnitPrice = unitPrice,
            LineSubtotal = lineSubtotal,
            LineVat = vatTreatment == VatTreatment.Standard ? lineSubtotal.MultiplyBy(vatRate) : Money.Zero,
        };
    }
}
