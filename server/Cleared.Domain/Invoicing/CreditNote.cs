using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// Unlike Invoice, a credit note has no draft phase: it's created complete, in one step,
// from a single confirmed action. There's nothing to incrementally edit — a correction
// document either states the correction or it doesn't exist yet.
public sealed class CreditNote : Entity
{
    // Inline-initialized rather than a constructor parameter, and populated by Create()
    // below rather than passed in directly — same reason as Invoice._lines: EF Core's
    // constructor-binding materialization cannot bind a navigation collection.
    private readonly List<CreditNoteLineItem> _lines = [];

    public Guid TenantId { get; }
    public Guid InvoiceId { get; }
    public string Number { get; }
    public string Reason { get; }

    // Not a constructor parameter — see the note on Tenant.CreatedAt / InvoiceLineItem.UnitPrice.
    // DateOnly hits the same EF Core constructor-binding limitation there, not just DateTimeOffset.
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

    // vatRate is the rate the original invoice snapshotted at its own Issue() — see the
    // note on CreditNoteLineItem.Create. number comes from the same claim-under-a-locked-
    // row mechanism as invoice numbers (see CreditNoteNumberAllocator), allocated by the
    // caller before this factory runs, since there's no draft phase in which to assign it
    // later the way Invoice does.
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
