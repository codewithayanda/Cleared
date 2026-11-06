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

    private Guid SeedCustomer(
        Guid tenantId, string? address = "1 Main Road, Cape Town", string? vatNumber = null, string name = "Beta Co")
    {
        var customer = Customer.Create(Guid.NewGuid(), tenantId, name, _now, vatNumber, address: address);
        _customerRepository.Seed(customer);
        return customer.Id;
    }

    private static CreateInvoiceRequest RequestFor(Guid customerId, decimal unitPrice, VatTreatment treatment) =>
        new(customerId, [new CreateInvoiceLineRequest("Consulting", 1m, unitPrice.ToString("F2"), treatment)]);

    private static UpdateInvoiceRequest UpdateFor(Guid customerId, decimal unitPrice, VatTreatment treatment) =>
        new(customerId, RequestFor(customerId, unitPrice, treatment).Lines);

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
    public async Task CreateAsync_RecordsTheClockTimeAsWhenTheInvoiceWasCreated()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService(new LateNightClock());

        var response = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.NotApplicable), CancellationToken.None);

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, response.Id, CancellationToken.None);
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 23, 30, 0, TimeSpan.Zero), stored!.CreatedAt);
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

    [Fact]
    public async Task IssueAsync_CopiesTheCustomerAndCompanyDetailsOntoTheInvoice()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId, vatNumber: "4987654321");
        var invoiceId = await IssuedInvoiceAsync(tenantId, customerId);

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, invoiceId, CancellationToken.None);

        Assert.Equal(new CustomerDetails("Beta Co", "1 Main Road, Cape Town", "4987654321"), stored!.IssuedTo);
        Assert.Equal(new SupplierDetails("Acme Ltd", null, "1 Acme Way, Johannesburg", "4123456789"), stored.IssuedBy);
    }

    [Fact]
    public async Task IssueAsync_AnInvoiceThatIsNotATaxInvoiceKeepsTheDetailsToo()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var invoiceId = await IssuedInvoiceAsync(tenantId, customerId);

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, invoiceId, CancellationToken.None);

        Assert.Equal("Beta Co", stored!.IssuedTo?.Name);
        Assert.Equal("Acme Ltd", stored.IssuedBy?.CompanyName);
    }

    [Fact]
    public async Task GetPdfAsync_AnIssuedInvoiceKeepsTheDetailsItWasIssuedWith()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId, vatNumber: "4987654321");
        var invoiceId = await IssuedInvoiceAsync(tenantId, customerId);

        await ChangeCustomerAndCompanyAsync(tenantId, customerId);
        await CreateService().GetPdfAsync(tenantId, invoiceId, CancellationToken.None);

        var document = _pdfRenderer.Last!;
        Assert.Equal("Beta Co", document.Customer.Name);
        Assert.Equal("1 Main Road, Cape Town", document.Customer.Address);
        Assert.Equal("4987654321", document.Customer.VatNumber);
        Assert.Equal("Acme Ltd", document.Tenant.CompanyName);
        Assert.Null(document.Tenant.TradingName);
        Assert.Equal("1 Acme Way, Johannesburg", document.Tenant.Address);
    }

    [Fact]
    public async Task GetPdfAsync_TheBankDetailsAreAlwaysTheCurrentOnes()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var invoiceId = await IssuedInvoiceAsync(tenantId, customerId);

        await ChangeCustomerAndCompanyAsync(tenantId, customerId);
        await CreateService().GetPdfAsync(tenantId, invoiceId, CancellationToken.None);

        var document = _pdfRenderer.Last!;
        Assert.Equal("Nedbank", document.Tenant.BankName);
        Assert.Equal("99887766", document.Tenant.BankAccountNumber);
        Assert.Equal("198765", document.Tenant.BankBranchCode);
    }

    [Fact]
    public async Task GetPdfAsync_ADraftShowsTheDetailsTheCustomerHasNow()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        await ChangeCustomerAndCompanyAsync(tenantId, customerId);
        await service.GetPdfAsync(tenantId, draft.Id, CancellationToken.None);

        var document = _pdfRenderer.Last!;
        Assert.Equal("Gamma Ltd", document.Customer.Name);
        Assert.Equal("9 High Street", document.Customer.Address);
        Assert.Equal("Acme Holdings", document.Tenant.CompanyName);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTheCustomerAndEveryLine()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var firstCustomerId = SeedCustomer(tenantId);
        var secondCustomerId = SeedCustomer(tenantId, name: "Gamma Ltd");
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(firstCustomerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var response = await service.UpdateAsync(
            tenantId,
            draft.Id,
            new UpdateInvoiceRequest(
                secondCustomerId,
                [
                    new CreateInvoiceLineRequest("Design", 2m, "50.00"),
                    new CreateInvoiceLineRequest("Hosting", 1m, "25.00", VatTreatment.ZeroRated),
                ]),
            CancellationToken.None);

        Assert.Equal(secondCustomerId, response!.CustomerId);
        Assert.Equal(["Design", "Hosting"], response.Lines.Select(line => line.Description));
        Assert.Equal("125.00", response.Subtotal);

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        Assert.Equal(secondCustomerId, stored!.CustomerId);
        Assert.Equal(["Design", "Hosting"], stored.Lines.Select(line => line.Description));
    }

    [Fact]
    public async Task UpdateAsync_NotRegisteredTenant_CoercesLineTreatmentToNotApplicable()
    {
        var tenantId = SeedTenant(VatStatus.NotRegistered, vatNumber: null);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.NotApplicable), CancellationToken.None);

        var response = await service.UpdateAsync(
            tenantId, draft.Id, UpdateFor(customerId, 80m, VatTreatment.Standard), CancellationToken.None);

        Assert.Equal(nameof(VatTreatment.NotApplicable), response!.Lines[0].VatTreatment);
    }

    [Fact]
    public async Task UpdateAsync_UnknownInvoice_ReturnsNull()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);

        var result = await CreateService().UpdateAsync(
            tenantId, Guid.NewGuid(), new UpdateInvoiceRequest(customerId, []), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_AnotherTenantsInvoice_ReturnsNullAndChangesNothing()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var intruderTenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var intruderCustomerId = SeedCustomer(intruderTenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var result = await service.UpdateAsync(
            intruderTenantId, draft.Id, UpdateFor(intruderCustomerId, 1m, VatTreatment.Standard), CancellationToken.None);

        Assert.Null(result);
        var stored = await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        Assert.Equal(customerId, stored!.CustomerId);
        Assert.Equal(Money.Zar(100m), Assert.Single(stored.Lines).UnitPrice);
    }

    [Fact]
    public async Task UpdateAsync_AnotherTenantsCustomer_ThrowsAndChangesNothing()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var otherTenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var othersCustomerId = SeedCustomer(otherTenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            tenantId, draft.Id, UpdateFor(othersCustomerId, 1m, VatTreatment.Standard), CancellationToken.None));

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        Assert.Equal(customerId, stored!.CustomerId);
        Assert.Equal("Consulting", Assert.Single(stored.Lines).Description);
    }

    [Fact]
    public async Task UpdateAsync_AnIssuedInvoice_ThrowsAndChangesNothing()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var invoiceId = await IssuedInvoiceAsync(tenantId, customerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().UpdateAsync(
            tenantId, invoiceId,
            new UpdateInvoiceRequest(customerId, [new CreateInvoiceLineRequest("Rewritten", 1m, "1.00")]),
            CancellationToken.None));

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, invoiceId, CancellationToken.None);
        Assert.Equal("Consulting", Assert.Single(stored!.Lines).Description);
    }

    [Fact]
    public async Task UpdateAsync_ALineThatIsRefused_LeavesTheDraftAsItWas()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var otherCustomerId = SeedCustomer(tenantId, name: "Gamma Ltd");
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateAsync(
            tenantId, draft.Id,
            new UpdateInvoiceRequest(
                otherCustomerId,
                [
                    new CreateInvoiceLineRequest("Fine", 1m, "10.00"),
                    new CreateInvoiceLineRequest("No quantity", 0m, "10.00"),
                ]),
            CancellationToken.None));

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        Assert.Equal(customerId, stored!.CustomerId);
        Assert.Equal("Consulting", Assert.Single(stored.Lines).Description);
    }

    [Fact]
    public async Task UpdateAsync_APriceThatIsNotAnAmount_ThrowsAndLeavesTheDraftAsItWas()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            tenantId, draft.Id,
            new UpdateInvoiceRequest(customerId, [new CreateInvoiceLineRequest("Item", 1m, "ten rand")]),
            CancellationToken.None));

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        Assert.Equal("Consulting", Assert.Single(stored!.Lines).Description);
    }

    [Fact]
    public async Task IssueAsync_AfterTheCustomerOfADraftWasChanged_CopiesTheNewCustomer()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var firstCustomerId = SeedCustomer(tenantId);
        var secondCustomerId = SeedCustomer(tenantId, name: "Gamma Ltd");
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(firstCustomerId, 100m, VatTreatment.Standard), CancellationToken.None);
        await service.UpdateAsync(
            tenantId, draft.Id, UpdateFor(secondCustomerId, 100m, VatTreatment.Standard), CancellationToken.None);

        await service.IssueAsync(
            tenantId, draft.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        var stored = await _invoiceRepository.GetByIdAsync(tenantId, draft.Id, CancellationToken.None);
        Assert.Equal("Gamma Ltd", stored!.IssuedTo?.Name);
    }

    [Fact]
    public async Task DeleteAsync_ADraft_RemovesItAndWritesAnAuditEntry()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var deleted = await service.DeleteAsync(tenantId, draft.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await service.GetByIdAsync(tenantId, draft.Id, CancellationToken.None));
        var entry = Assert.Single(_auditLogRepository.Entries);
        Assert.Equal("Invoice", entry.EntityType);
        Assert.Equal(draft.Id, entry.EntityId);
        Assert.Equal("Deleted", entry.Action);
        Assert.Contains("1 line", entry.Details);
        Assert.Contains("100.00", entry.Details);
    }

    [Fact]
    public async Task DeleteAsync_UnknownInvoice_ReturnsFalseAndWritesNothing()
    {
        var tenantId = SeedTenant(VatStatus.Registered);

        var deleted = await CreateService().DeleteAsync(tenantId, Guid.NewGuid(), CancellationToken.None);

        Assert.False(deleted);
        Assert.Empty(_auditLogRepository.Entries);
    }

    [Fact]
    public async Task DeleteAsync_AnotherTenantsDraft_ReturnsFalseAndKeepsIt()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var intruderTenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);

        var deleted = await service.DeleteAsync(intruderTenantId, draft.Id, CancellationToken.None);

        Assert.False(deleted);
        Assert.NotNull(await service.GetByIdAsync(tenantId, draft.Id, CancellationToken.None));
        Assert.Empty(_auditLogRepository.Entries);
    }

    [Fact]
    public async Task DeleteAsync_AnIssuedInvoice_ThrowsAndKeepsIt()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var customerId = SeedCustomer(tenantId);
        var invoiceId = await IssuedInvoiceAsync(tenantId, customerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService().DeleteAsync(tenantId, invoiceId, CancellationToken.None));

        Assert.NotNull(await CreateService().GetByIdAsync(tenantId, invoiceId, CancellationToken.None));
        Assert.DoesNotContain(_auditLogRepository.Entries, entry => entry.Action == "Deleted");
    }

    private async Task<Guid> IssuedInvoiceAsync(Guid tenantId, Guid customerId)
    {
        var service = CreateService();
        var draft = await service.CreateAsync(
            tenantId, RequestFor(customerId, 100m, VatTreatment.Standard), CancellationToken.None);
        await service.IssueAsync(
            tenantId, draft.Id, new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        return draft.Id;
    }

    // Edits the way the app does: the customer through its form, the company through its name and settings.
    private async Task ChangeCustomerAndCompanyAsync(Guid tenantId, Guid customerId)
    {
        var customer = (await _customerRepository.GetByIdAsync(tenantId, customerId, CancellationToken.None))!;
        customer.UpdateDetails("Gamma Ltd", "4111111111", null, "9 High Street");

        var tenant = (await _tenantRepository.GetByIdAsync(tenantId, CancellationToken.None))!;
        tenant.Rename("Acme Holdings", "Acme Trading");
        tenant.UpdateProfile("5 New Road", "Nedbank", "99887766", "198765");
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
