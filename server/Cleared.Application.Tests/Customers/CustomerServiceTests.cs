using Cleared.Application.Customers;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tests.Customers;

public class CustomerServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid _tenantId = Guid.NewGuid();

    private readonly FakeCustomerRepository _repository = new();

    private CustomerService CreateService() => new(_repository, new FakeUnitOfWork());

    private Customer Seed(Guid? tenantId = null)
    {
        var customer = Customer.Create(
            Guid.NewGuid(), tenantId ?? _tenantId, "Beta Co", _now,
            vatNumber: "4987654321", email: "ap@beta.test", address: "1 Main Road, Cape Town");
        _repository.Seed(customer);

        return customer;
    }

    private async Task<Customer> StoredAsync(Customer customer) =>
        (await _repository.GetByIdAsync(customer.TenantId, customer.Id, CancellationToken.None))!;

    [Fact]
    public async Task GetAsync_ACustomerOfTheTenant_ReturnsIt()
    {
        var customer = Seed();

        var response = await CreateService().GetAsync(_tenantId, customer.Id, CancellationToken.None);

        Assert.Equal(new CustomerResponse(
            customer.Id, _tenantId, "Beta Co", "4987654321", "ap@beta.test", "1 Main Road, Cape Town"), response);
    }

    [Fact]
    public async Task GetAsync_AnotherTenantsCustomer_ReturnsNull()
    {
        var customer = Seed(tenantId: Guid.NewGuid());

        var response = await CreateService().GetAsync(_tenantId, customer.Id, CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateAsync_ChangesEveryDetailAndGivesTheNewOnesBack()
    {
        var customer = Seed();
        var request = new UpdateCustomerRequest("Gamma Ltd", "4111111111", "accounts@gamma.test", "9 High Street");

        var response = await CreateService().UpdateAsync(_tenantId, customer.Id, request, CancellationToken.None);

        Assert.Equal(new CustomerResponse(
            customer.Id, _tenantId, "Gamma Ltd", "4111111111", "accounts@gamma.test", "9 High Street"), response);
        var stored = await StoredAsync(customer);
        Assert.Equal("Gamma Ltd", stored.Name);
        Assert.Equal("9 High Street", stored.Address);
    }

    [Fact]
    public async Task UpdateAsync_CanClearTheOptionalDetails()
    {
        var customer = Seed();

        await CreateService().UpdateAsync(
            _tenantId, customer.Id, new UpdateCustomerRequest("Beta Co", null, null, null), CancellationToken.None);

        var stored = await StoredAsync(customer);
        Assert.Null(stored.VatNumber);
        Assert.Null(stored.Email);
        Assert.Null(stored.Address);
    }

    [Fact]
    public async Task UpdateAsync_AnotherTenantsCustomer_ReturnsNullAndChangesNothing()
    {
        var customer = Seed(tenantId: Guid.NewGuid());

        var response = await CreateService().UpdateAsync(
            _tenantId, customer.Id, new UpdateCustomerRequest("Hijacked", null, null, null), CancellationToken.None);

        Assert.Null(response);
        Assert.Equal("Beta Co", (await StoredAsync(customer)).Name);
    }

    [Fact]
    public async Task UpdateAsync_ABlankName_ThrowsAndChangesNothing()
    {
        var customer = Seed();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().UpdateAsync(
            _tenantId, customer.Id, new UpdateCustomerRequest(" ", null, null, null), CancellationToken.None));

        Assert.Equal("Beta Co", (await StoredAsync(customer)).Name);
    }

    [Fact]
    public async Task UpdateAsync_AnEmailThatCannotBeReal_ThrowsAndChangesNothing()
    {
        var customer = Seed();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().UpdateAsync(
            _tenantId, customer.Id, new UpdateCustomerRequest("Beta Co", null, "not-an-email", null), CancellationToken.None));

        Assert.Equal("ap@beta.test", (await StoredAsync(customer)).Email);
    }
}
