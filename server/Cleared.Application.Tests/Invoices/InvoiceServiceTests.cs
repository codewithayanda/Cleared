using Cleared.Application.Invoices;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tests.Invoices;

public class InvoiceServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly _today = new(2026, 8, 28);

    private readonly FakeTenantRepository _tenantRepository = new();
    private readonly FakeCustomerRepository _customerRepository = new();
    private readonly FakeInvoiceRepository _invoiceRepository = new();

    private InvoiceService CreateService() => new(
        _invoiceRepository, _tenantRepository, _customerRepository, new FakeVatRateRepository(),
        new FakeInvoiceNumberAllocator(), new FakeUnitOfWork());

    private Guid SeedTenant(VatStatus vatStatus, string? vatNumber = "4123456789")
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme Ltd", vatStatus, vatNumber, _now);
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
        // No address on file at all — would fail tax-invoice validation, but this tenant
        // isn't issuing a tax invoice, so that validation must never run.
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
    public async Task IssueAsync_UnknownInvoiceId_ReturnsNull()
    {
        var tenantId = SeedTenant(VatStatus.Registered);
        var service = CreateService();

        var result = await service.IssueAsync(
            tenantId, Guid.NewGuid(), new IssueInvoiceRequest(_today, _today.AddDays(30)), CancellationToken.None);

        Assert.Null(result);
    }
}
