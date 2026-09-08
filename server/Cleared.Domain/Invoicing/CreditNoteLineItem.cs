using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class CreditNoteLineItem : Entity
{
    public Guid CreditNoteId { get; }

    // The original invoice line this credits — a credit note line is never freestanding,
    // it always corrects something that was actually charged.
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

    // vatRate is the ORIGINAL invoice's VatRateApplied, not today's rate — a credit note
    // corrects a specific invoice and must use the rate that invoice actually charged, or
    // a credit issued after a rate change would silently stop balancing against it.
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
