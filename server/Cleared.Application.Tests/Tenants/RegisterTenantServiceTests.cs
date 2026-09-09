using Cleared.Application.Tenants;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tests.Tenants;

public class RegisterTenantServiceTests
{
    private readonly FakeTenantRepository _tenantRepository = new();

    private RegisterTenantService CreateService() => new(_tenantRepository, new FakeUnitOfWork());

    [Fact]
    public async Task RegisterAsync_WithProfileDetails_ReturnsThemOnTheResponse()
    {
        var service = CreateService();
        var request = new RegisterTenantRequest(
            "Acme Ltd", VatStatus.NotRegistered, null, null,
            Address: "1 Main Road, Cape Town", BankName: "FNB", BankAccountNumber: "123", BankBranchCode: "250655");

        var response = await service.RegisterAsync(request, CancellationToken.None);

        Assert.Equal("1 Main Road, Cape Town", response.Address);
        Assert.Equal("FNB", response.BankName);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownTenant_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateProfileAsync_UnknownTenant_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.UpdateProfileAsync(
            Guid.NewGuid(), new UpdateTenantProfileRequest(null, null, null, null), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidRequest_PersistsAndReturnsUpdatedProfile()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync(
            new RegisterTenantRequest("Acme Ltd", VatStatus.NotRegistered, null, null), CancellationToken.None);

        var updated = await service.UpdateProfileAsync(
            registered.Id,
            new UpdateTenantProfileRequest("1 Main Road, Cape Town", "Standard Bank", "987654321", "051001"),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("1 Main Road, Cape Town", updated.Address);
        Assert.Equal("Standard Bank", updated.BankName);
        Assert.Equal("987654321", updated.BankAccountNumber);
        Assert.Equal("051001", updated.BankBranchCode);

        var refetched = await service.GetByIdAsync(registered.Id, CancellationToken.None);
        Assert.Equal("Standard Bank", refetched!.BankName);
    }
}
