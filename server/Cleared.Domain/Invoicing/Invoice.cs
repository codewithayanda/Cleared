using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

public sealed class Invoice : Entity
{
    private readonly List<InvoiceLineItem> _lines = [];

    public Guid TenantId { get; }
    public Guid CustomerId { get; }
    public InvoiceStatus Status { get; private set; }
    public string? Number { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }

    public IReadOnlyList<InvoiceLineItem> Lines => _lines;

    public Money Subtotal =>
        _lines.Aggregate(Money.Zero, (runningTotal, line) => runningTotal.Add(line.LineSubtotal));

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

    public void AddLine(Guid lineId, string description, decimal quantity, Money unitPrice)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Cannot add a line to an invoice that has already been issued.");
        }

        _lines.Add(InvoiceLineItem.Create(lineId, Id, description, quantity, unitPrice));
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

    public void Issue(string number, DateOnly issueDate, DateOnly dueDate)
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

        Number = number;
        IssueDate = issueDate;
        DueDate = dueDate;
        Status = InvoiceStatus.Issued;
    }

    public bool IsOverdue(DateOnly today) =>
        Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid
        && DueDate is { } dueDate
        && dueDate < today;
}
