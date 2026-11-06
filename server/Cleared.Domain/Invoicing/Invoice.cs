using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class Invoice : Entity
{
    private readonly List<InvoiceLineItem> _lines = [];

    public Guid TenantId { get; }
    public Guid CustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
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

    // Copied from the customer and the company at Issue() and never changed after that, so a later edit
    // to either cannot rewrite an invoice that has gone out. A draft has none yet.
    public string? CustomerName { get; private set; }
    public string? CustomerAddress { get; private set; }
    public string? CustomerVatNumber { get; private set; }
    public string? SupplierCompanyName { get; private set; }
    public string? SupplierTradingName { get; private set; }
    public string? SupplierAddress { get; private set; }
    public string? SupplierVatNumber { get; private set; }

    public CustomerDetails? IssuedTo =>
        CustomerName is null ? null : new CustomerDetails(CustomerName, CustomerAddress, CustomerVatNumber);

    public SupplierDetails? IssuedBy => SupplierCompanyName is null
        ? null
        : new SupplierDetails(SupplierCompanyName, SupplierTradingName, SupplierAddress, SupplierVatNumber);

    // In the order they were entered. The database returns them in no order that means anything, so this
    // sorts by position wherever the invoice was loaded from.
    public IReadOnlyList<InvoiceLineItem> Lines => [.. _lines.OrderBy(line => line.Position)];

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

    public static Invoice CreateDraft(Guid id, Guid tenantId, Guid customerId, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must belong to a tenant.", nameof(tenantId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must have a customer.", nameof(customerId));
        }

        if (createdAt == default)
        {
            throw new ArgumentException("An invoice must record when it was created.", nameof(createdAt));
        }

        return new Invoice(id, tenantId, customerId) { CreatedAt = createdAt };
    }

    public void AddLine(Guid lineId, string description, decimal quantity, Money unitPrice, VatTreatment vatTreatment)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Cannot add a line to an invoice that has already been issued.");
        }

        var position = _lines.Count == 0 ? 0 : _lines.Max(line => line.Position) + 1;

        _lines.Add(InvoiceLineItem.Create(lineId, Id, position, description, quantity, unitPrice, vatTreatment));
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

    // Replaces the customer and every line at once, because the form sends them as a whole. Every line
    // is built before anything changes, so if one is refused the draft stays exactly as it was.
    public void Revise(Guid customerId, IReadOnlyList<DraftLine> lines)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft invoice can be changed.");
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("An invoice must have a customer.", nameof(customerId));
        }

        var replacements = lines
            .Select((l, index) =>
                InvoiceLineItem.Create(l.Id, Id, index, l.Description, l.Quantity, l.UnitPrice, l.VatTreatment))
            .ToList();

        CustomerId = customerId;
        _lines.Clear();
        _lines.AddRange(replacements);
    }

    // An issued invoice is part of the record and is corrected with a credit note, never removed.
    public void EnsureDeletable()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only a draft invoice can be deleted. An issued invoice is corrected with a credit note.");
        }
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
        decimal vatRate,
        CustomerDetails customer,
        SupplierDetails supplier)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(supplier);

        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft invoice can be issued.");
        }

        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            throw new ArgumentException("An issued invoice must name its customer.", nameof(customer));
        }

        if (string.IsNullOrWhiteSpace(supplier.CompanyName))
        {
            throw new ArgumentException("An issued invoice must name the company that issued it.", nameof(supplier));
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
        CustomerName = customer.Name;
        CustomerAddress = customer.Address;
        CustomerVatNumber = customer.VatNumber;
        SupplierCompanyName = supplier.CompanyName;
        SupplierTradingName = supplier.TradingName;
        SupplierAddress = supplier.Address;
        SupplierVatNumber = supplier.VatNumber;
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
