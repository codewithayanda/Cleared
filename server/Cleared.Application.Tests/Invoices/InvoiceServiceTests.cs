using Cleared.Application.Abstractions;
using Cleared.Application.Invoices;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Payments;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tests.Invoices;

public class InvoiceServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly _today = new(2026, 8, 28);

    private readonly FakeTenantRepository _tenantRepository = new();
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly FakeInvoiceRepository _invoiceRepository = new();
    private readonly FakeAuditLogRepository _auditLogRepository = new();
    private readonly FakePaymentRepository _paymentRepository = new();
    private readonly FakeInvoicePdfRenderer _pdfRenderer = new();

    private InvoiceService CreateService(IClock? clock = null) => new(
        _invoiceRepository, new FakeInvoiceLock(_invoiceRepository), _tenantRepository,
        _customerRepository, new FakeVatRateRepository(),
        new FakeInvoiceNumberAllocator(), _auditLogRepository, _paymentRepository, _pdfRenderer,
        new FakeUnitOfWork(), new FakeCurrentUserContext(Guid.NewGuid()), clock ?? new FakeClock(_today));

    private Guid SeedTenant(VatStatus vatStatus, string? vatNumber = "4123456789", string? address = "1 Acme Way, Johannesburg")
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme Ltd", vatStatus, vatNumber, _now, address: address);
        _tenantRepository.Seed(tenant);
        return tenant.Id;
    }

    private Guid SeedCustomer(Guid tenantId, string? address = "1 Main Road, Cape Town", string? vatNumber = null)
    {
        var customer = Customer.Create(Guid.NewGuid(), tenantId, "Beta Co", _now, vatNumber, address: address);
        _customerRepository.Seed(customer);
        return customer.Id;
    }

    private static CreateInvoiceRequest RequestFor(Guid customerId, decimal unitPrice, VatTreatment treatment) =>
        new(customerId, [new CreateInvoiceLineRequest("Consulting", 1m, unitPrice.ToString("F2"), treatment)]);

    [Fact]
    public async Task CreateAsync_NotRegisteredTenant_CoercesLineTreatmentToNotApplicable()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();

        var response = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        Assert.Equal(nameof(VatTreatment.NotApplicable), response.Lines[0].VatTreatment);
    }

    [Fact]
    public async Task CreateAsync_RegisteredTenant_KeepsRequestedTreatment()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();

        var response = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.ZeroRated), CancellationToken.None);

        Assert.Equal(nameof(VatTreatment.ZeroRated), response.Lines[0].VatTreatment);
    }

    [Fact]
    public async Task IssueAsync_RegisteredTenantMissingOwnAddress_ThrowsWithFieldNamed()
    {
        var tenantId = SeedTenant(VatStatus.Registered, address: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TaxInvoiceValidationException>(() =>
            service.IssueAsync(tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None));

        Assert.Contains(exception.MissingFields, field => field.Contains("company address"));
    }

    [Fact]
    public async Task IssueAsync_RegisteredTenantCustomerMissingAddress_ThrowsWithFieldNamed()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId, address: null);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TaxInvoiceValidationException>(() =>
            service.IssueAsync(tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None));

        Assert.Contains("customer address", exception.MissingFields);
    }

    [Fact]
    public async Task IssueAsync_RegisteredTenantBelowThresholdWithNoCustomerVatNumber_Succeeds()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var issued = await service.IssueAsync(
            tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal(nameof(DocumentType.TaxInvoice), issued!.DocumentType);
    }

    [Fact]
    public async Task IssueAsync_RegisteredTenantAtThresholdWithNoCustomerVatNumber_Throws()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        // 5000 * 1.15 comfortably clears the R5,000 threshold on the VAT-inclusive total.
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 5000m, VatTreatment.Standard), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TaxInvoiceValidationException>(() =>
            service.IssueAsync(tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None));

        Assert.Contains(exception.MissingFields, field => field.Contains("VAT number"));
    }

    [Fact]
    public async Task IssueAsync_RegisteredTenantAtThresholdWithCustomerVatNumber_Succeeds()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId, vatNumber: "4987654321");
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 5000m, VatTreatment.Standard), CancellationToken.None);

        var issued = await service.IssueAsync(
            tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal(nameof(DocumentType.TaxInvoice), issued!.DocumentType);
    }

    [Fact]
    public async Task IssueAsync_NotRegisteredTenant_IssuesAsPlainInvoiceWithoutTaxInvoiceValidation()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        // No address on file, which would fail tax-invoice validation. This tenant is not
        // issuing a tax invoice, so that validation must not run.
        var customerId = SeedCustomer(tenantId, address: null);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var issued = await service.IssueAsync(
            tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal(nameof(DocumentType.Invoice), issued!.DocumentType);
        Assert.Equal("0.00", issued.VatTotal);
    }

    [Fact]
    public async Task IssueAsync_Succeeds_WritesAnAuditLogEntry()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        await service.IssueAsync(tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        var entry = Assert.Single(_auditLogRepository.Entries);
        Assert.Equal("Issued", entry.Action);
        Assert.Equal(invoice.Id, entry.EntityId);
    }

    [Fact]
    public async Task IssueAsync_UnknownInvoiceId_ReturnsNull()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var service = CreateService();

        var result = await service.IssueAsync(
            tenantId, Guid.NewGuid(), new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IssueAsync_NoPaymentsYet_ReportsFullBalanceDue()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var issued = await service.IssueAsync(
            tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Equal("0.00", issued!.AmountPaid);
        Assert.Equal(issued.Total, issued.BalanceDue);
    }

    [Fact]
    public async Task GetByIdAsync_WithARecordedPayment_ReturnsAmountPaidAndBalanceDue()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var invoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);
        await service.IssueAsync(
            tenantId, invoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);
        // Total is 115.00 (100 + 15% VAT); this leaves 65.00 outstanding.
        _paymentRepository.Seed(
            Payment.RecordManual(Guid.NewGuid(), tenantId, invoice.Id, Money.Zar(50m), _today, _now));

        var fetched = await service.GetByIdAsync(tenantId, invoice.Id, CancellationToken.None);

        Assert.Equal("50.00", fetched!.AmountPaid);
        Assert.Equal("65.00", fetched.BalanceDue);
    }

    [Fact]
    public async Task ListAsync_PaymentsOnOneInvoice_DoNotLeakIntoAnothersBalance()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var paidInvoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);
        var untouchedInvoice = await service.CreateAsync(
            tenantId, RequestFor(customerId, 200m, VatTreatment.Standard), CancellationToken.None);
        await service.IssueAsync(
            tenantId, paidInvoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);
        await service.IssueAsync(
            tenantId, untouchedInvoice.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);
        _paymentRepository.Seed(
            Payment.RecordManual(Guid.NewGuid(), tenantId, paidInvoice.Id, Money.Zar(115m), _today, _now));

        var invoices = await service.ListAsync(tenantId, CancellationToken.None);

        Assert.Equal("115.00", invoices.Single(i => i.Id == paidInvoice.Id).AmountPaid);
        Assert.Equal("0.00", invoices.Single(i => i.Id == paidInvoice.Id).BalanceDue);
        Assert.Equal("0.00", invoices.Single(i => i.Id == untouchedInvoice.Id).AmountPaid);
        Assert.Equal("230.00", invoices.Single(i => i.Id == untouchedInvoice.Id).BalanceDue);
    }

    [Fact]
    public async Task ListAsync_IssuedInvoicePastItsDueDate_IsReportedOverdue()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var invoice = await IssueDueOnAsync(service, tenantId, customerId, _today.AddDays(-1));

        var listed = (await service.ListAsync(tenantId, CancellationToken.None)).Single(i => i.Id == invoice.Id);
        var fetched = await service.GetByIdAsync(tenantId, invoice.Id, CancellationToken.None);

        Assert.True(listed.IsOverdue);
        Assert.True(fetched!.IsOverdue);
    }

    [Fact]
    public async Task ListAsync_InvoiceDueTodayOrLater_IsNotOverdue()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var dueToday = await IssueDueOnAsync(service, tenantId, customerId, _today);
        var dueTomorrow = await IssueDueOnAsync(service, tenantId, customerId, _today.AddDays(1));

        var listed = await service.ListAsync(tenantId, CancellationToken.None);

        Assert.False(listed.Single(i => i.Id == dueToday.Id).IsOverdue);
        Assert.False(listed.Single(i => i.Id == dueTomorrow.Id).IsOverdue);
    }

    [Fact]
    public async Task CreateAsync_ADraft_IsNeverOverdue()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();

        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.NotApplicable), CancellationToken.None);

        Assert.False(draft.IsOverdue);
    }

    [Fact]
    public async Task Overdue_IsJudgedByTheSouthAfricanDateAndNotTheUtcOne()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService(new LateNightClock());
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.NotApplicable), CancellationToken.None);

        var issued = await service.IssueAsync(
            tenantId,
            draft.Id,
            new IssueInvoiceRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 28)),
            CancellationToken.None);
        var fetched = await service.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        var listed = await service.ListAsync(tenantId, CancellationToken.None);

        Assert.True(issued!.IsOverdue);
        Assert.True(fetched!.IsOverdue);
        Assert.True(listed.Single(i => i.Id == draft.Id).IsOverdue);
    }

    [Fact]
    public async Task GetPdfAsync_PartPaidInvoice_GivesTheBuilderThePaymentsAndTodayInSouthAfrica()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService(new LateNightClock());
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);
        await service.IssueAsync(
            tenantId, draft.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);
        _paymentRepository.Seed(
            Payment.RecordManual(Guid.NewGuid(), tenantId, draft.Id, Money.Zar(50m), _today.AddDays(-1), _now));
        (await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None))!
            .RecordPaymentTotal(Money.Zar(50m));

        var pdf = await service.GetPdfAsync(tenantId, draft.Id, CancellationToken.None);

        Assert.NotNull(pdf);
        var content = Assert.IsType<InvoiceDocument>(_pdfRenderer.Last);
        var payment = Assert.Single(content.Payments);
        Assert.Equal("Balance due", content.TotalLabel);
        Assert.Equal("65.00", content.TotalAmount);
        Assert.Equal("Payments received as at 29 Aug 2026", content.PaymentsHeading);
        Assert.Equal("27 Aug 2026", payment.Date);
        Assert.Equal("50.00", payment.Amount);
    }

    [Fact]
    public async Task GetPdfAsync_UnknownInvoice_ReturnsNullAndDrawsNothing()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var service = CreateService();

        var pdf = await service.GetPdfAsync(tenantId, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(pdf);
        Assert.Null(_pdfRenderer.Last);
    }

    private static async Task<InvoiceResponse> IssueDueOnAsync(
        InvoiceService service, Guid tenantId, Guid customerId, DateOnly dueDate)
    {
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.NotApplicable), CancellationToken.None);

        return (await service.IssueAsync(
            tenantId, draft.Id, new IssueInvoiceRequest(dueDate.AddDays(-30), dueDate), CancellationToken.None))!;
    }

    // Half past eleven at night in UTC is already tomorrow in South Africa.
    private sealed class LateNightClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 28, 23, 30, 0, TimeSpan.Zero);

        public DateOnly Today { get; } = new(2026, 8, 29);
    }

    [Fact]
    public async Task CreateAsync_CustomerBelongingToAnotherTenant_ThrowsAndCreatesNothing()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var otherTenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var othersCustomerId = SeedCustomer(otherTenantId);
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            tenantId, RequestFor(othersCustomerId, 100m, VatTreatment.Standard), CancellationToken.None));

        Assert.Empty(await service.ListAsync(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_UnknownCustomer_Throws()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            tenantId, RequestFor(Guid.NewGuid(), 100m, VatTreatment.Standard), CancellationToken.None));
    }
}
