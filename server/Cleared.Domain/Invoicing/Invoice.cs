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

    // The date the goods or services were supplied, usually but not always the same day as
    // IssueDate (e.g. invoicing in arrears). The VAT rate is looked up against this date,
    // not IssueDate: invoice in June for a supply before an April rate change, April applies.
    public DateOnly? SupplyDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    // Snapshotted at Issue() and never re-read afterwards. See VatRate.
    public decimal? VatRateApplied { get; private set; }

    public IReadOnlyList<InvoiceLineItem> Lines => _lines;

    // Computed from the lines, not stored. Safe once issued because AddLine/RemoveLine
    // reject anything past Draft, so the inputs are frozen at issue.
    public Money Subtotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineSubtotal));

    public Money VatTotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineVat));

    public Money Total => Subtotal.Add(VatTotal);

    // What Total would be at the given rate, without mutating anything. Tax-invoice field
    // validation needs the total for the R5,000 threshold and can fail, so it has to run
    // before Issue() commits to a rate and a status change.
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

    // documentType and vatRate come from the caller (Application layer): DocumentType
    // depends on the tenant's VatStatus and vatRate on a VatRate lookup, neither of which
    // Invoice can reach. Tax-invoice validation against the Customer is one level up too.
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

    // An issued tax invoice cannot be edited or deleted; the only lawful correction is a
    // credit note. CreditNoteService decides whether a note fully covers the invoice before
    // calling this. Reachable from Paid: a customer can pay first and need a credit later.
    // TODO: crediting a Paid invoice flips the status but moves no money. No refund path yet.
    public void Cancel()
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid))
        {
            throw new InvalidOperationException(
                "Only an issued, partially paid or paid invoice can be cancelled, and only via a credit note.");
        }

        Status = InvoiceStatus.Cancelled;
    }

    // totalPaid is the sum of every payment against this invoice, including the one that
    // triggered this call. PaymentService computes it; Invoice holds no reference to Payment.
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
