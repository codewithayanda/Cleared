using Cleared.Application.Payments;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.Tests.Payments;

public class PaymentServiceTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _customerId = Guid.NewGuid();
    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly DateOnly _issueDate = new(2026, 9, 1);
    private static readonly DateOnly _receivedAt = new(2026, 9, 10);
    private const decimal StandardRate = 0.15m;

    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakePaymentRepository _paymentRepository = new();
    private readonly FakeAuditLogRepository _auditLogRepository = new();

    private PaymentService CreateService() => new(
        _invoiceRepository, _paymentRepository, _auditLogRepository, new FakeUnitOfWork(),
        new FakeCurrentUserContext(_userId), new FakeClock(_receivedAt));

    private Invoice SeedIssuedInvoice(decimal unitPrice = 1000m)
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId);
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(unitPrice), VatTreatment.Standard);
        invoice.Issue("INV-2026-0001", _issueDate, _issueDate, _issueDate.AddDays(30), DocumentType.TaxInvoice, StandardRate);
        _invoiceRepository.Seed(invoice);
        return invoice;
    }

    [Fact]
    public async Task RecordAsync_UnknownInvoice_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.RecordAsync(
            _tenantId, Guid.NewGuid(), new RecordPaymentRequest("100.00", _receivedAt, null), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordAsync_DraftInvoice_Throws()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), _tenantId, _customerId);
        invoice.AddLine(Guid.NewGuid(), "Item", 1m, Money.Zar(100m), VatTreatment.Standard);
        _invoiceRepository.Seed(invoice);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("100.00", _receivedAt, null), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_PartialAmount_LeavesInvoicePartiallyPaidAndReturnsPaymentDetails()
    {
        var invoice = SeedIssuedInvoice(); // Total = 1150.00
        var service = CreateService();

        var payment = await service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("500.00", _receivedAt, "EFT ref 123"), CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal("500.00", payment.Amount);
        Assert.Equal("Manual", payment.Method);
        Assert.Equal("EFT ref 123", payment.Reference);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
    }

    [Fact]
    public async Task RecordAsync_FullAmount_MarksInvoicePaid()
    {
        var invoice = SeedIssuedInvoice(); // Total = 1150.00
        var service = CreateService();

        await service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("1150.00", _receivedAt, null), CancellationToken.None);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task RecordAsync_SecondPaymentCompletingTheTotal_MarksInvoicePaid()
    {
        var invoice = SeedIssuedInvoice(); // Total = 1150.00
        var service = CreateService();

        await service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("500.00", _receivedAt, null), CancellationToken.None);
        await service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("650.00", _receivedAt, null), CancellationToken.None);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task RecordAsync_AmountExceedingWhatsOwed_Throws()
    {
        var invoice = SeedIssuedInvoice(); // Total = 1150.00
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("1150.01", _receivedAt, null), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_ValidPayment_WritesAnAuditLogEntry()
    {
        var invoice = SeedIssuedInvoice();
        var service = CreateService();

        await service.RecordAsync(
            _tenantId, invoice.Id, new RecordPaymentRequest("500.00", _receivedAt, null), CancellationToken.None);

        var entry = Assert.Single(_auditLogRepository.Entries);
        Assert.Equal(_userId, entry.UserId);
        Assert.Equal("PaymentRecorded", entry.Action);
        Assert.Equal(invoice.Id, entry.EntityId);
    }
}
