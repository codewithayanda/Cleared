using Cleared.Application.CreditNotes;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.Tests.CreditNotes;

public class CreditNoteServiceTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();
    private static readonly DateOnly _issueDate = new(2026, 8, 28);
    private static readonly DateOnly _today = new(2026, 9, 1);
    private const decimal StandardRate = 0.15m;

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeCreditNoteRepository _creditNoteRepository = new();
    private readonly FakeAuditLogRepository _auditLogRepository = new();

    private CreditNoteService CreateService() => new(
        _invoiceRepository, _creditNoteRepository, new FakeCreditNoteNumberAllocator(),
        _auditLogRepository, new FakeUnitOfWork(), new FakeCurrentUserContext(Guid.NewGuid()),
        new FakeClock(_today));

    private Invoice SeedIssuedInvoice(params (decimal Quantity, decimal UnitPrice)[] lines)
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId);
        var lineIds = new List<Guid>();

        foreach (var (quantity, unitPrice) in lines)
        {
            var lineId = Guid.NewGuid();
            lineIds.Add(lineId);
            invoice.AddLine(lineId, "Item", quantity, Money.Zar(unitPrice), VatTreatment.Standard);
        }

        invoice.Issue("INV-2026-0001", _issueDate, _issueDate, _issueDate.AddDays(30), DocumentType.TaxInvoice, StandardRate);
        _invoiceRepository.Seed(invoice);

        return invoice;
    }

    [Fact]
    public async Task CreateAsync_UnknownInvoice_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            _tenantId, Guid.NewGuid(), new CreateCreditNoteRequest("Return", []), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_InvoiceBelongsToAnotherTenant_ReturnsNull()
    {
        var invoice = SeedIssuedInvoice((1m, 1000m));
        var service = CreateService();
        var request = new CreateCreditNoteRequest(
            "Return", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 1m)]);

        var result = await service.CreateAsync(Guid.NewGuid(), invoice.Id, request, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_DraftInvoice_Throws()
    {
        var draft = Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId);
        draft.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        _invoiceRepository.Seed(draft);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            _tenantId, draft.Id, new CreateCreditNoteRequest("Return", []), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_UnknownLineId_Throws()
    {
        var invoice = SeedIssuedInvoice((1m, 1000m));
        var service = CreateService();
        var request = new CreateCreditNoteRequest("Return", [new CreateCreditNoteLineRequest(Guid.NewGuid(), 1m)]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(_tenantId, invoice.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_QuantityExceedsOriginalLine_Throws()
    {
        var invoice = SeedIssuedInvoice((1m, 1000m));
        var service = CreateService();
        var request = new CreateCreditNoteRequest(
            "Return", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 2m)]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(_tenantId, invoice.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_FullCredit_CancelsTheInvoice()
    {
        var invoice = SeedIssuedInvoice((1m, 1000m));
        var service = CreateService();
        var request = new CreateCreditNoteRequest(
            "Faulty goods", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 1m)]);

        var creditNote = await service.CreateAsync(_tenantId, invoice.Id, request, CancellationToken.None);

        Assert.NotNull(creditNote);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal("1150.00", creditNote.Total);
        Assert.StartsWith("CN-", creditNote.Number);
    }

    [Fact]
    public async Task CreateAsync_PartialCredit_LeavesInvoiceIssued()
    {
        var invoice = SeedIssuedInvoice((10m, 100m));
        var service = CreateService();
        var request = new CreateCreditNoteRequest(
            "Damaged units", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 3m)]);

        await service.CreateAsync(_tenantId, invoice.Id, request, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    [Fact]
    public async Task CreateAsync_SecondCreditReachingFullQuantity_CancelsTheInvoice()
    {
        var invoice = SeedIssuedInvoice((10m, 100m));
        var service = CreateService();
        var lineId = invoice.Lines[0].Id;

        await service.CreateAsync(
            _tenantId, invoice.Id, new CreateCreditNoteRequest("First batch", [new CreateCreditNoteLineRequest(lineId, 4m)]),
            CancellationToken.None);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);

        await service.CreateAsync(
            _tenantId, invoice.Id, new CreateCreditNoteRequest("Remaining batch", [new CreateCreditNoteLineRequest(lineId, 6m)]),
            CancellationToken.None);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public async Task CreateAsync_AgainstPartiallyPaidInvoice_Succeeds()
    {
        var invoice = SeedIssuedInvoice((1m, 1000m));
        invoice.RecordPaymentTotal(Money.Zar(500m)); // Total 1150 -> PartiallyPaid
        var service = CreateService();
        var request = new CreateCreditNoteRequest(
            "Faulty goods", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 1m)]);

        var creditNote = await service.CreateAsync(_tenantId, invoice.Id, request, CancellationToken.None);

        Assert.NotNull(creditNote);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public async Task CreateAsync_ValidCredit_WritesAnAuditLogEntry()
    {
        var invoice = SeedIssuedInvoice((1m, 1000m));
        var service = CreateService();
        var request = new CreateCreditNoteRequest(
            "Faulty goods", [new CreateCreditNoteLineRequest(invoice.Lines[0].Id, 1m)]);

        await service.CreateAsync(_tenantId, invoice.Id, request, CancellationToken.None);

        var entry = Assert.Single(_auditLogRepository.Entries);
        Assert.Equal("CreditNoteIssued", entry.Action);
        Assert.Equal(invoice.Id, entry.EntityId);
    }

    [Fact]
    public async Task CreateAsync_SecondCreditExceedingWhatsLeft_Throws()
    {
        var invoice = SeedIssuedInvoice((10m, 100m));
        var service = CreateService();
        var lineId = invoice.Lines[0].Id;

        await service.CreateAsync(
            _tenantId, invoice.Id, new CreateCreditNoteRequest("First batch", [new CreateCreditNoteLineRequest(lineId, 8m)]),
            CancellationToken.None);

        var secondRequest = new CreateCreditNoteRequest("Too much", [new CreateCreditNoteLineRequest(lineId, 3m)]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(_tenantId, invoice.Id, secondRequest, CancellationToken.None));
    }
}
