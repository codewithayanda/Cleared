using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Domain.Tests.Invoicing;

public class InvoiceTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private static readonly DateOnly _issueDate = new(2026, 8, 28);
    private static readonly DateOnly _dueDate = new(2026, 9, 27);
    private const decimal StandardRate = 0.15m;

    private static Invoice NewDraft() => Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId);

    private static void IssueAsTaxInvoice(
        Invoice invoice, string number = "INV-2026-0001", decimal vatRate = StandardRate) =>
        invoice.Issue(number, _issueDate, _issueDate, _dueDate, DocumentType.TaxInvoice, vatRate);

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
        Assert.Null(invoice.DocumentType);
        Assert.Null(invoice.IssueDate);
        Assert.Null(invoice.SupplyDate);
        Assert.Null(invoice.DueDate);
        Assert.Null(invoice.VatRateApplied);
    }

    [Fact]
    public void AddLine_ToDraft_AddsLineAndUpdatesSubtotal()
    {
        var invoice = NewDraft();

        invoice.AddLine(Guid.NewGuid(), "Consulting — August", 10m, Money.Zar(150m), VatTreatment.Standard);

        Assert.Single(invoice.Lines);
        Assert.Equal(Money.Zar(1500m), invoice.Subtotal);
    }

    [Fact]
    public void AddLine_BlankDescription_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<ArgumentException>(() =>
            invoice.AddLine(Guid.NewGuid(), " ", 1m, Money.Zar(100m), VatTreatment.Standard));
    }

    [Fact]
    public void AddLine_ZeroOrNegativeQuantity_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            invoice.AddLine(Guid.NewGuid(), "Item", 0m, Money.Zar(100m), VatTreatment.Standard));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            invoice.AddLine(Guid.NewGuid(), "Item", -1m, Money.Zar(100m), VatTreatment.Standard));
    }

    [Fact]
    public void AddLine_MultipleLines_SubtotalIsTheSum()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item A", 2m, Money.Zar(50m), VatTreatment.Standard);
        invoice.AddLine(Guid.NewGuid(), "Item B", 1m, Money.Zar(30m), VatTreatment.Standard);

        Assert.Equal(Money.Zar(130m), invoice.Subtotal);
    }

    [Fact]
    public void AddLine_AfterIssue_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);

        Assert.Throws<InvalidOperationException>(() =>
            invoice.AddLine(Guid.NewGuid(), "Another item", 1m, Money.Zar(50m), VatTreatment.Standard));
    }

    [Fact]
    public void RemoveLine_ExistingLine_RemovesIt()
    {
        var invoice = NewDraft();
        var lineId = Guid.NewGuid();
        invoice.AddLine(lineId, "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

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
        invoice.AddLine(lineId, "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);

        Assert.Throws<InvalidOperationException>(() => invoice.RemoveLine(lineId));
    }

    [Fact]
    public void Issue_DraftWithLines_SetsNumberDatesDocumentTypeRateAndStatus()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        invoice.Issue("INV-2026-0001", _issueDate, _issueDate, _dueDate, DocumentType.TaxInvoice, StandardRate);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal("INV-2026-0001", invoice.Number);
        Assert.Equal(DocumentType.TaxInvoice, invoice.DocumentType);
        Assert.Equal(_issueDate, invoice.IssueDate);
        Assert.Equal(_issueDate, invoice.SupplyDate);
        Assert.Equal(_dueDate, invoice.DueDate);
        Assert.Equal(StandardRate, invoice.VatRateApplied);
    }

    [Fact]
    public void Issue_SupplyDateDiffersFromIssueDate_BothAreStoredSeparately()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        var supplyDate = new DateOnly(2026, 3, 15);

        invoice.Issue("INV-2026-0001", _issueDate, supplyDate, _dueDate, DocumentType.TaxInvoice, StandardRate);

        Assert.Equal(_issueDate, invoice.IssueDate);
        Assert.Equal(supplyDate, invoice.SupplyDate);
    }

    [Fact]
    public void Issue_WithNoLines_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<InvalidOperationException>(() => IssueAsTaxInvoice(invoice));
    }

    [Fact]
    public void Issue_AlreadyIssued_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);

        Assert.Throws<InvalidOperationException>(() => IssueAsTaxInvoice(invoice, "INV-2026-0002"));
    }

    [Fact]
    public void Issue_BlankNumber_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        Assert.Throws<ArgumentException>(() => IssueAsTaxInvoice(invoice, " "));
    }

    [Fact]
    public void Issue_DueDateBeforeIssueDate_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        Assert.Throws<ArgumentException>(() =>
            invoice.Issue(
                "INV-2026-0001", _issueDate, _issueDate, new DateOnly(2026, 8, 1), DocumentType.TaxInvoice, StandardRate));
    }

    // --- VAT computation, by treatment (D12) ---

    [Fact]
    public void Issue_StandardLine_ChargesVatAtTheGivenRate()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Consulting", 1m, Money.Zar(1000m), VatTreatment.Standard);

        IssueAsTaxInvoice(invoice);

        Assert.Equal(Money.Zar(150m), invoice.VatTotal);
        Assert.Equal(Money.Zar(1150m), invoice.Total);
        Assert.Equal(VatTreatment.Standard, invoice.Lines[0].VatTreatment);
        Assert.Equal(Money.Zar(150m), invoice.Lines[0].LineVat);
        Assert.Equal(Money.Zar(1150m), invoice.Lines[0].LineTotal);
    }

    [Fact]
    public void Issue_ZeroRatedLine_ChargesNoVat()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Export sale", 1m, Money.Zar(1000m), VatTreatment.ZeroRated);

        IssueAsTaxInvoice(invoice);

        Assert.Equal(Money.Zero, invoice.VatTotal);
        Assert.Equal(Money.Zar(1000m), invoice.Total);
        Assert.Equal(Money.Zero, invoice.Lines[0].LineVat);
    }

    [Fact]
    public void Issue_ExemptLine_ChargesNoVat()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Residential rent", 1m, Money.Zar(1000m), VatTreatment.Exempt);

        IssueAsTaxInvoice(invoice);

        Assert.Equal(Money.Zero, invoice.VatTotal);
        Assert.Equal(Money.Zar(1000m), invoice.Total);
    }

    [Fact]
    public void Issue_NotApplicableLine_ChargesNoVatRegardlessOfRate()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.NotApplicable);

        invoice.Issue("INV-2026-0001", _issueDate, _issueDate, _dueDate, DocumentType.Invoice, StandardRate);

        Assert.Equal(Money.Zero, invoice.VatTotal);
        Assert.Equal(Money.Zar(1000m), invoice.Total);
        Assert.Equal(DocumentType.Invoice, invoice.DocumentType);
    }

    [Fact]
    public void Issue_MixedTreatmentLines_EachLineTaxedAccordingToItsOwnTreatment()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Standard item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        invoice.AddLine(Guid.NewGuid(), "Zero-rated export", 1m, Money.Zar(500m), VatTreatment.ZeroRated);
        invoice.AddLine(Guid.NewGuid(), "Exempt service", 1m, Money.Zar(200m), VatTreatment.Exempt);

        IssueAsTaxInvoice(invoice);

        // Only the Standard line contributes VAT: 1000 * 0.15 = 150.
        Assert.Equal(Money.Zar(150m), invoice.VatTotal);
        Assert.Equal(Money.Zar(1700m), invoice.Subtotal);
        Assert.Equal(Money.Zar(1850m), invoice.Total);
    }

    [Fact]
    public void Issue_RoundsVatPerLineThenSums_NotOnTheGrandTotal()
    {
        // Two lines whose per-line VAT each rounds to a different cent than rounding the
        // combined subtotal once would — proves rounding happens per line (ADR 0006),
        // not by applying the rate to Subtotal as a whole.
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Line A", 1m, Money.Zar(10.01m), VatTreatment.Standard);
        invoice.AddLine(Guid.NewGuid(), "Line B", 1m, Money.Zar(10.02m), VatTreatment.Standard);

        IssueAsTaxInvoice(invoice);

        var expectedLineAVat = Money.Zar(10.01m).MultiplyBy(StandardRate);
        var expectedLineBVat = Money.Zar(10.02m).MultiplyBy(StandardRate);

        Assert.Equal(expectedLineAVat.Add(expectedLineBVat), invoice.VatTotal);
    }

    [Fact]
    public void ProspectiveTotal_BeforeIssue_ComputesWithoutMutatingTheInvoice()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);

        var prospective = invoice.ProspectiveTotal(StandardRate);

        Assert.Equal(Money.Zar(1150m), prospective);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(Money.Zero, invoice.VatTotal);
    }

    // --- Cancellation via credit note (D7) ---

    [Fact]
    public void Cancel_IssuedInvoice_TransitionsToCancelled()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);

        invoice.Cancel();

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public void Cancel_DraftInvoice_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<InvalidOperationException>(invoice.Cancel);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);
        invoice.Cancel();

        Assert.Throws<InvalidOperationException>(invoice.Cancel);
    }

    // --- Derived overdue ---

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
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        invoice.Issue(
            "INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
            DocumentType.TaxInvoice, StandardRate);

        Assert.True(invoice.IsOverdue(new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void IsOverdue_IssuedButNotYetPastDueDate_ReturnsFalse()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        invoice.Issue(
            "INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
            DocumentType.TaxInvoice, StandardRate);

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 8, 15)));
    }
}
