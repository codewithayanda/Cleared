using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class Invoice : Entity
{
    private readonly List<InvoiceLineItem> _lines = [];

    public Guid TenantId { get; }
    public Guid CustomerId { get; }
    public InvoiceStatus Status { get; private set; }
    public string? Number { get; private set; }
    public DocumentType? DocumentType { get; private set; }
    public DateOnly? IssueDate { get; private set; }

    // The date the goods/services were actually supplied — usually the same day as
    // IssueDate, but not always (e.g. invoicing in arrears). This is the date the
    // applicable VAT rate is looked up against, not IssueDate: invoice in June for a
    // supply made before an April rate change, and the April rate applies.
    public DateOnly? SupplyDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    // Snapshotted at Issue() and never re-read afterwards — see VatRate.
    public decimal? VatRateApplied { get; private set; }

    public IReadOnlyList<InvoiceLineItem> Lines => _lines;

    // Subtotal/VatTotal/Total are computed from the lines rather than stored. That's safe
    // even for an issued invoice: AddLine/RemoveLine already refuse to run once Status is
    // no longer Draft, so the lines a issued invoice carries are frozen the moment it's
    // issued — a computed total over frozen inputs can never drift from what was issued.
    public Money Subtotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineSubtotal));

    public Money VatTotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineVat));

    public Money Total => Subtotal.Add(VatTotal);

    // What Total would be if issued at the given rate, without mutating anything. Needed
    // because tax-invoice field validation (which needs the total, for the R5,000
    // abridged-vs-full threshold) must run — and can fail — before Issue() commits to a
    // rate and a status change, not after.
    public Money ProspectiveTotal(decimal vatRate)
    {
        var vatTotal = _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.PreviewVat(vatRate)));

        return Subtotal.Add(vatTotal);
    }

    private Invoice(Guid id, Guid tenantId, Guid customerId) : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        Status = InvoiceStatus.Draft;
    }

    public static Invoice CreateDraft(Guid id, Guid tenantId, Guid customerId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must belong to a tenant.", nameof(tenantId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must have a customer.", nameof(customerId));
        }

        return new Invoice(id, tenantId, customerId);
    }

    public void AddLine(Guid lineId, string description, decimal quantity, Money unitPrice, VatTreatment vatTreatment)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Cannot add a line to an invoice that has already been issued.");
        }

        _lines.Add(InvoiceLineItem.Create(lineId, Id, description, quantity, unitPrice, vatTreatment));
    }

    public void RemoveLine(Guid lineId)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                "Cannot remove a line from an invoice that has already been issued.");
        }

        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            throw new InvalidOperationException($"No line with id '{lineId}' exists on this invoice.");
        }

        _lines.Remove(line);
    }

    // documentType and vatRate are supplied by the caller (Application layer) rather than
    // derived here: DocumentType depends on the tenant's VatStatus and vatRate on a
    // VatRate lookup, and Invoice has no way to reach either of those — it only knows
    // about itself and its own lines, not the tenant or reference data.  Tax-invoice field
    // validation against the customer (name/address/VAT number) happens one level up for
    // the same reason: Invoice cannot see the Customer aggregate.
    public void Issue(
        string number,
        DateOnly issueDate,
        DateOnly supplyDate,
        DateOnly dueDate,
        DocumentType documentType,
        decimal vatRate)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft invoice can be issued.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("An invoice must have at least one line before it can be issued.");
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("An issued invoice must have a number.", nameof(number));
        }

        if (dueDate < issueDate)
        {
            throw new ArgumentException("The due date cannot be before the issue date.", nameof(dueDate));
        }

        foreach (var line in _lines)
        {
            line.ApplyVatRate(vatRate);
        }

        Number = number;
        IssueDate = issueDate;
        SupplyDate = supplyDate;
        DueDate = dueDate;
        DocumentType = documentType;
        VatRateApplied = vatRate;
        Status = InvoiceStatus.Issued;
    }

    // A tax invoice can never be edited or deleted once issued — the only lawful
    // correction is a credit note (see CreditNote). Whether a given credit note fully
    // covers this invoice (and so should cancel it) or only partially credits it is a
    // cross-aggregate question CreditNoteService answers before calling this. Reachable
    // from PartiallyPaid and Paid too, not just Issued: a customer can pay first and need
    // a credit later. Crediting a Paid invoice implies money is now owed back — this only
    // flips the status; it does not itself move any money, which is a real, deliberate
    // gap until there's a way to actually send a refund.
    public void Cancel()
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid))
        {
            throw new InvalidOperationException(
                "Only an issued, partially paid or paid invoice can be cancelled, and only via a credit note.");
        }

        Status = InvoiceStatus.Cancelled;
    }

    // totalPaid is the sum of every succeeded payment against this invoice, including
    // whichever one triggered this call — computed by the caller (PaymentService), which
    // can see every Payment; Invoice itself has no reference back to them (same reasoning
    // as CreditNote — see Issue()'s note on cross-aggregate data).
    public void RecordPaymentTotal(Money totalPaid)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid))
        {
            throw new InvalidOperationException("Payments can only be recorded against an issued invoice.");
        }

        if (totalPaid.Amount > Total.Amount)
        {
            throw new InvalidOperationException(
                "Recording this payment would take the total paid above the invoice total.");
        }

        Status = totalPaid.Amount >= Total.Amount ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    public bool IsOverdue(DateOnly today) =>
        Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid
        && DueDate is { } dueDate
        && dueDate < today;
}
