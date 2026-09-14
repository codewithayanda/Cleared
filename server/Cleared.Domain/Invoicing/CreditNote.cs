using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// No draft phase, unlike Invoice: a credit note is created complete, in one step, from a
// single confirmed action. There is nothing to edit incrementally.
public sealed class CreditNote : Entity
{
    // Populated by Create(), not bound by the constructor: EF Core's constructor binding
    // cannot bind a navigation collection. Same as Invoice._lines.
    private readonly List<CreditNoteLineItem> _lines = [];

    public Guid TenantId { get; }
    public Guid InvoiceId { get; }
    public string Number { get; }
    public string Reason { get; }

    // Private setter, not a constructor parameter. DateOnly hits the same EF Core
    // constructor-binding limit as DateTimeOffset. See InvoiceLineItem.UnitPrice.
    public DateOnly IssueDate { get; private set; }

    public IReadOnlyList<CreditNoteLineItem> Lines => _lines;

    public Money Subtotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineSubtotal));

    public Money VatTotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineVat));

    public Money Total => Subtotal.Add(VatTotal);

    private CreditNote(Guid id, Guid tenantId, Guid invoiceId, string number, string reason) : base(id)
    {
        TenantId = tenantId;
        InvoiceId = invoiceId;
        Number = number;
        Reason = reason;
    }

    // vatRate is the rate the original invoice snapshotted at Issue(). number is allocated
    // by the caller before this runs (see CreditNoteNumberAllocator), since there is no
    // draft phase in which to assign it later.
    public static CreditNote Create(
        Guid id,
        Guid tenantId,
        Guid invoiceId,
        string number,
        string reason,
        DateOnly issueDate,
        decimal vatRate,
        IReadOnlyList<CreditNoteLineRequest> lines)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("A credit note must have a number.", nameof(number));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A credit note must state a reason.", nameof(reason));
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("A credit note must have at least one line.", nameof(lines));
        }

        var creditNote = new CreditNote(id, tenantId, invoiceId, number, reason)
        {
            IssueDate = issueDate,
        };

        foreach (var line in lines)
        {
            creditNote._lines.Add(CreditNoteLineItem.Create(
                Guid.NewGuid(), id, line.InvoiceLineItemId, line.Description, line.Quantity,
                line.UnitPrice, line.VatTreatment, vatRate));
        }

        return creditNote;
    }
}
