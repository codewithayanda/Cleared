using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Domain.Tests.Invoicing;

public class InvoiceTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private static Invoice NewDraft() => Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId);

    [Fact]
    public void CreateDraft_EmptyTenantId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Invoice.CreateDraft(Guid.NewGuid(), Guid.Empty, _customerId));
    }

    [Fact]
    public void CreateDraft_EmptyCustomerId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Invoice.CreateDraft(Guid.NewGuid(), _tenantId, Guid.Empty));
    }

    [Fact]
    public void CreateDraft_StartsInDraftStatusWithNoLines()
    {
        var invoice = NewDraft();

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Empty(invoice.Lines);
        Assert.Null(invoice.Number);
        Assert.Null(invoice.IssueDate);
        Assert.Null(invoice.DueDate);
    }

    [Fact]
    public void AddLine_ToDraft_AddsLineAndUpdatesSubtotal()
    {
        var invoice = NewDraft();

        invoice.AddLine(Guid.NewGuid(), "Consulting — August", 10m, Money.Zar(150m));

        Assert.Single(invoice.Lines);
        Assert.Equal(Money.Zar(1500m), invoice.Subtotal);
    }

    [Fact]
    public void AddLine_BlankDescription_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<ArgumentException>(() => invoice.AddLine(Guid.NewGuid(), " ", 1m, Money.Zar(100m)));
    }

    [Fact]
    public void AddLine_ZeroOrNegativeQuantity_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.AddLine(Guid.NewGuid(), "Item", 0m, Money.Zar(100m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.AddLine(Guid.NewGuid(), "Item", -1m, Money.Zar(100m)));
    }

    [Fact]
    public void AddLine_MultipleLines_SubtotalIsTheSum()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item A", 2m, Money.Zar(50m));
        invoice.AddLine(Guid.NewGuid(), "Item B", 1m, Money.Zar(30m));

        Assert.Equal(Money.Zar(130m), invoice.Subtotal);
    }

    [Fact]
    public void AddLine_AfterIssue_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));
        invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 27));

        Assert.Throws<InvalidOperationException>(() =>
            invoice.AddLine(Guid.NewGuid(), "Another item", 1m, Money.Zar(50m)));
    }

    [Fact]
    public void RemoveLine_ExistingLine_RemovesIt()
    {
        var invoice = NewDraft();
        var lineId = Guid.NewGuid();
        invoice.AddLine(lineId, "Item", 1m, Money.Zar(100m));

        invoice.RemoveLine(lineId);

        Assert.Empty(invoice.Lines);
    }

    [Fact]
    public void RemoveLine_UnknownLineId_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<InvalidOperationException>(() => invoice.RemoveLine(Guid.NewGuid()));
    }

    [Fact]
    public void RemoveLine_AfterIssue_Throws()
    {
        var invoice = NewDraft();
        var lineId = Guid.NewGuid();
        invoice.AddLine(lineId, "Item", 1m, Money.Zar(100m));
        invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 27));

        Assert.Throws<InvalidOperationException>(() => invoice.RemoveLine(lineId));
    }

    [Fact]
    public void Issue_DraftWithLines_SetsNumberIssueDateDueDateAndStatus()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));

        var issueDate = new DateOnly(2026, 8, 28);
        var dueDate = new DateOnly(2026, 9, 27);

        invoice.Issue("INV-2026-0001", issueDate, dueDate);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal("INV-2026-0001", invoice.Number);
        Assert.Equal(issueDate, invoice.IssueDate);
        Assert.Equal(dueDate, invoice.DueDate);
    }

    [Fact]
    public void Issue_WithNoLines_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<InvalidOperationException>(() =>
            invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 27)));
    }

    [Fact]
    public void Issue_AlreadyIssued_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));
        invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 27));

        Assert.Throws<InvalidOperationException>(() =>
            invoice.Issue("INV-2026-0002", new DateOnly(2026, 8, 29), new DateOnly(2026, 9, 28)));
    }

    [Fact]
    public void Issue_BlankNumber_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));

        Assert.Throws<ArgumentException>(() =>
            invoice.Issue(" ", new DateOnly(2026, 8, 28), new DateOnly(2026, 9, 27)));
    }

    [Fact]
    public void Issue_DueDateBeforeIssueDate_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));

        Assert.Throws<ArgumentException>(() =>
            invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void IsOverdue_DraftInvoice_IsNeverOverdue()
    {
        var invoice = NewDraft();

        Assert.False(invoice.IsOverdue(new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void IsOverdue_IssuedAndPastDueDate_ReturnsTrue()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));
        invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15));

        Assert.True(invoice.IsOverdue(new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void IsOverdue_IssuedButNotYetPastDueDate_ReturnsFalse()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m));
        invoice.Issue("INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15));

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 8, 15)));
    }
}
