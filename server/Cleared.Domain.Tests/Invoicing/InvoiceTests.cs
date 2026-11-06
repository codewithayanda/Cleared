using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Domain.Tests.Invoicing;

public class InvoiceTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();

    private static readonly DateTimeOffset _createdAt = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly CustomerDetails _customerDetails = new("Beta Co", "1 Main Road, Cape Town", "4987654321");
    private static readonly SupplierDetails _supplierDetails =
        new("Acme Ltd", "Acme Trading", "1 Acme Way, Johannesburg", "4123456789");
    private static readonly DateOnly _issueDate = new(2026, 8, 28);
    private static readonly DateOnly _dueDate = new(2026, 9, 27);
    private const decimal StandardRate = 0.15m;

    private static Invoice NewDraft() => Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId, _createdAt);

    private static void IssueAsTaxInvoice(
        Invoice invoice, string number = "INV-2026-0001", decimal vatRate = StandardRate) =>
        invoice.Issue(
            number, _issueDate, _issueDate, _dueDate, DocumentType.TaxInvoice, vatRate, _customerDetails, _supplierDetails);

    [Fact]
    public void CreateDraft_EmptyTenantId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Invoice.CreateDraft(Guid.NewGuid(), Guid.Empty, _customerId, _createdAt));
    }

    [Fact]
    public void CreateDraft_EmptyCustomerId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Invoice.CreateDraft(Guid.NewGuid(), _tenantId, Guid.Empty, _createdAt));
    }

    [Fact]
    public void CreateDraft_RecordsWhenItWasCreated()
    {
        Assert.Equal(_createdAt, NewDraft().CreatedAt);
    }

    [Fact]
    public void CreateDraft_NoCreationTime_Throws()
    {
        Assert.Throws<ArgumentException>(() => Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId, default));
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

        invoice.AddLine(Guid.NewGuid(), "Consulting, August", 10m, Money.Zar(150m), VatTreatment.Standard);

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

        invoice.Issue(
            "INV-2026-0001", _issueDate, _issueDate, _dueDate, DocumentType.TaxInvoice, StandardRate, _customerDetails, _supplierDetails);

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

        invoice.Issue(
            "INV-2026-0001", _issueDate, supplyDate, _dueDate, DocumentType.TaxInvoice, StandardRate, _customerDetails, _supplierDetails);

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
                "INV-2026-0001", _issueDate, _issueDate, new DateOnly(2026, 8, 1), DocumentType.TaxInvoice, StandardRate,
                _customerDetails, _supplierDetails));
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

        invoice.Issue(
            "INV-2026-0001", _issueDate, _issueDate, _dueDate, DocumentType.Invoice, StandardRate, _customerDetails, _supplierDetails);

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
        // combined subtotal once would, so this fails if rounding moves to Subtotal.
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
    public void Cancel_PartiallyPaidInvoice_Succeeds()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice); // Total = 1150.00
        invoice.RecordPaymentTotal(Money.Zar(500m));

        invoice.Cancel();

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public void Cancel_PaidInvoice_Succeeds()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice); // Total = 1150.00
        invoice.RecordPaymentTotal(Money.Zar(1150m));

        invoice.Cancel();

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
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

    // --- Recording payments ---

    [Fact]
    public void RecordPaymentTotal_DraftInvoice_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        Assert.Throws<InvalidOperationException>(() => invoice.RecordPaymentTotal(Money.Zar(100m)));
    }

    [Fact]
    public void RecordPaymentTotal_LessThanTotal_TransitionsToPartiallyPaid()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice); // Total = 1150.00

        invoice.RecordPaymentTotal(Money.Zar(500m));

        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
    }

    [Fact]
    public void RecordPaymentTotal_EqualToTotal_TransitionsToPaid()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice); // Total = 1150.00

        invoice.RecordPaymentTotal(Money.Zar(1150m));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void RecordPaymentTotal_MoreThanTotal_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice); // Total = 1150.00

        Assert.Throws<InvalidOperationException>(() => invoice.RecordPaymentTotal(Money.Zar(1150.01m)));
    }

    [Fact]
    public void RecordPaymentTotal_SecondPaymentCompletingTheTotal_TransitionsFromPartiallyPaidToPaid()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(1000m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice); // Total = 1150.00
        invoice.RecordPaymentTotal(Money.Zar(500m));

        invoice.RecordPaymentTotal(Money.Zar(1150m));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void RecordPaymentTotal_CancelledInvoice_Throws()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);
        invoice.Cancel();

        Assert.Throws<InvalidOperationException>(() => invoice.RecordPaymentTotal(Money.Zar(100m)));
    }

    // --- Derived overdue ---

    [Fact]
    public void Revise_ReplacesTheCustomerAndEveryLine()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Old", 1m, Money.Zar(100m), VatTreatment.Standard);
        var newCustomerId = Guid.NewGuid();

        invoice.Revise(
            newCustomerId,
            [
                new DraftLine(Guid.NewGuid(), "New A", 2m, Money.Zar(50m), VatTreatment.Standard),
                new DraftLine(Guid.NewGuid(), "New B", 1m, Money.Zar(25m), VatTreatment.ZeroRated),
            ]);

        Assert.Equal(newCustomerId, invoice.CustomerId);
        Assert.Equal(["New A", "New B"], invoice.Lines.Select(line => line.Description));
        Assert.Equal([0, 1], invoice.Lines.Select(line => line.Position));
        Assert.Equal(Money.Zar(125m), invoice.Subtotal);
    }

    [Fact]
    public void AddLine_NumbersTheLinesInTheOrderTheyAreAdded()
    {
        var invoice = NewDraft();

        invoice.AddLine(Guid.NewGuid(), "First", 1m, Money.Zar(10m), VatTreatment.Standard);
        invoice.AddLine(Guid.NewGuid(), "Second", 1m, Money.Zar(10m), VatTreatment.Standard);
        invoice.AddLine(Guid.NewGuid(), "Third", 1m, Money.Zar(10m), VatTreatment.Standard);

        Assert.Equal(["First", "Second", "Third"], invoice.Lines.Select(line => line.Description));
        Assert.Equal([0, 1, 2], invoice.Lines.Select(line => line.Position));
    }

    [Fact]
    public void AddLine_AfterALineWasRemoved_GoesAfterTheLastOne()
    {
        var invoice = NewDraft();
        var second = Guid.NewGuid();
        invoice.AddLine(Guid.NewGuid(), "First", 1m, Money.Zar(10m), VatTreatment.Standard);
        invoice.AddLine(second, "Second", 1m, Money.Zar(10m), VatTreatment.Standard);
        invoice.AddLine(Guid.NewGuid(), "Third", 1m, Money.Zar(10m), VatTreatment.Standard);
        invoice.RemoveLine(second);

        invoice.AddLine(Guid.NewGuid(), "Fourth", 1m, Money.Zar(10m), VatTreatment.Standard);

        Assert.Equal(["First", "Third", "Fourth"], invoice.Lines.Select(line => line.Description));
    }

    [Fact]
    public void Revise_ALineThatIsRefused_LeavesTheDraftExactlyAsItWas()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Old", 1m, Money.Zar(100m), VatTreatment.Standard);

        Assert.Throws<ArgumentOutOfRangeException>(() => invoice.Revise(
            Guid.NewGuid(),
            [
                new DraftLine(Guid.NewGuid(), "Fine", 1m, Money.Zar(10m), VatTreatment.Standard),
                new DraftLine(Guid.NewGuid(), "No quantity", 0m, Money.Zar(10m), VatTreatment.Standard),
            ]));

        Assert.Equal(_customerId, invoice.CustomerId);
        Assert.Equal("Old", Assert.Single(invoice.Lines).Description);
    }

    [Fact]
    public void Revise_AnInvoiceThatIsIssued_ThrowsAndChangesNothing()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Old", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);

        Assert.Throws<InvalidOperationException>(() => invoice.Revise(Guid.NewGuid(), []));

        Assert.Equal(_customerId, invoice.CustomerId);
        Assert.Equal("Old", Assert.Single(invoice.Lines).Description);
    }

    [Fact]
    public void Revise_WithNoCustomer_Throws()
    {
        Assert.Throws<ArgumentException>(() => NewDraft().Revise(Guid.Empty, []));
    }

    [Fact]
    public void EnsureDeletable_ADraft_IsAllowed()
    {
        Assert.Null(Record.Exception(NewDraft().EnsureDeletable));
    }

    [Theory]
    [InlineData(InvoiceStatus.Issued)]
    [InlineData(InvoiceStatus.PartiallyPaid)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public void EnsureDeletable_AnInvoiceThatHasBeenIssued_Throws(InvoiceStatus status)
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        IssueAsTaxInvoice(invoice);

        switch (status)
        {
            case InvoiceStatus.PartiallyPaid:
                invoice.RecordPaymentTotal(Money.Zar(10m));
                break;
            case InvoiceStatus.Paid:
                invoice.RecordPaymentTotal(invoice.Total);
                break;
            case InvoiceStatus.Cancelled:
                invoice.Cancel();
                break;
        }

        Assert.Equal(status, invoice.Status);
        Assert.Throws<InvalidOperationException>(invoice.EnsureDeletable);
    }

    [Fact]
    public void Issue_KeepsWhoTheInvoiceWasIssuedToAndBy()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        IssueAsTaxInvoice(invoice);

        Assert.Equal(_customerDetails, invoice.IssuedTo);
        Assert.Equal(_supplierDetails, invoice.IssuedBy);
    }

    [Fact]
    public void CreateDraft_HasNoIssuedPartiesYet()
    {
        var draft = NewDraft();

        Assert.Null(draft.IssuedTo);
        Assert.Null(draft.IssuedBy);
    }

    [Fact]
    public void Issue_WithoutACustomerName_ThrowsAndLeavesTheDraftAlone()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        Assert.Throws<ArgumentException>(() => invoice.Issue(
            "INV-2026-0001", _issueDate, _issueDate, _dueDate, DocumentType.TaxInvoice, StandardRate,
            _customerDetails with { Name = " " }, _supplierDetails));

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.IssuedTo);
    }

    [Fact]
    public void Issue_WithoutACompanyName_ThrowsAndLeavesTheDraftAlone()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);

        Assert.Throws<ArgumentException>(() => invoice.Issue(
            "INV-2026-0001", _issueDate, _issueDate, _dueDate, DocumentType.TaxInvoice, StandardRate,
            _customerDetails, _supplierDetails with { CompanyName = "" }));

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.IssuedBy);
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
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        invoice.Issue(
            "INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
            DocumentType.TaxInvoice, StandardRate, _customerDetails, _supplierDetails);

        Assert.True(invoice.IsOverdue(new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void IsOverdue_IssuedButNotYetPastDueDate_ReturnsFalse()
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        invoice.Issue(
            "INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
            DocumentType.TaxInvoice, StandardRate, _customerDetails, _supplierDetails);

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 8, 15)));
    }

    [Fact]
    public void IsOverdue_PartiallyPaidAndPastDueDate_ReturnsTrue()
    {
        var invoice = IssuedDueOn(new DateOnly(2026, 8, 15));
        invoice.RecordPaymentTotal(Money.Zar(40m));

        Assert.True(invoice.IsOverdue(new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void IsOverdue_PaidInFullAndPastDueDate_ReturnsFalse()
    {
        var invoice = IssuedDueOn(new DateOnly(2026, 8, 15));
        invoice.RecordPaymentTotal(invoice.Total);

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void IsOverdue_CancelledAndPastDueDate_ReturnsFalse()
    {
        var invoice = IssuedDueOn(new DateOnly(2026, 8, 15));
        invoice.Cancel();

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 8, 16)));
    }

    private static Invoice IssuedDueOn(DateOnly dueDate)
    {
        var invoice = NewDraft();
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        invoice.Issue(
            "INV-2026-0001", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), dueDate,
            DocumentType.TaxInvoice, StandardRate, _customerDetails, _supplierDetails);

        return invoice;
    }
}
