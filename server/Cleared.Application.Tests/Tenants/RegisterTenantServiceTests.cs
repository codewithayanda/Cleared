using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Cleared.Application.Tests.TestDoubles;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tests.Tenants;

public class RegisterTenantServiceTests
{
    private readonly FakeTenantRepository _tenantRepository = new();

    private RegisterTenantService CreateService(bool emailConfirmed = true) => new(
        _tenantRepository,
        new FakeUnitOfWork(),
        new FakeCurrentUserContext(Guid.NewGuid()),
        new FakeEmailVerification(emailConfirmed));

    private static Task<TenantResponse> RegisterWithBankDetailsAsync(RegisterTenantService service) =>
        service.RegisterAsync(
            new RegisterTenantRequest(
                "Acme Ltd", VatStatus.NotRegistered, null, null,
                Address: "1 Main Road", BankName: "FNB", BankAccountNumber: "123", BankBranchCode: "250655"),
            CancellationToken.None);

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

    [Fact]
    public async Task UpdateProfileAsync_BankDetailsChangedBeforeTheEmailIsConfirmed_IsRefusedAndNothingChanges()
    {
        var service = CreateService(emailConfirmed: false);
        var registered = await RegisterWithBankDetailsAsync(service);

        await Assert.ThrowsAsync<EmailNotVerifiedException>(() => service.UpdateProfileAsync(
            registered.Id, new UpdateTenantProfileRequest("2 Side Street", "Thief Bank", "999", "000"), CancellationToken.None));

        var unchanged = await service.GetByIdAsync(registered.Id, CancellationToken.None);
        Assert.Equal("FNB", unchanged!.BankName);
        Assert.Equal("1 Main Road", unchanged.Address);
    }

    [Fact]
    public async Task UpdateProfileAsync_FirstBankDetailsBeforeTheEmailIsConfirmed_IsRefused()
    {
        var service = CreateService(emailConfirmed: false);
        var registered = await service.RegisterAsync(
            new RegisterTenantRequest("Acme Ltd", VatStatus.NotRegistered, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<EmailNotVerifiedException>(() => service.UpdateProfileAsync(
            registered.Id, new UpdateTenantProfileRequest(null, "FNB", "123", "250655"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProfileAsync_AddressChangedBeforeTheEmailIsConfirmed_IsAllowed()
    {
        var service = CreateService(emailConfirmed: false);
        var registered = await RegisterWithBankDetailsAsync(service);

        var updated = await service.UpdateProfileAsync(
            registered.Id, new UpdateTenantProfileRequest("2 Side Street", "FNB", "123", "250655"), CancellationToken.None);

        Assert.Equal("2 Side Street", updated!.Address);
        Assert.Equal("FNB", updated.BankName);
    }

    [Theory]
    [InlineData("FNB", "123", "250655")]
    [InlineData("  FNB  ", "123 ", " 250655")]
    public async Task UpdateProfileAsync_BankDetailsSentUnchangedBeforeTheEmailIsConfirmed_IsAllowed(
        string bankName, string account, string branch)
    {
        var service = CreateService(emailConfirmed: false);
        var registered = await RegisterWithBankDetailsAsync(service);

        var updated = await service.UpdateProfileAsync(
            registered.Id, new UpdateTenantProfileRequest("1 Main Road", bankName, account, branch), CancellationToken.None);

        Assert.NotNull(updated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateProfileAsync_NoBankDetailsWhereNoneAreStored_IsNotAChange(string? empty)
    {
        var service = CreateService(emailConfirmed: false);
        var registered = await service.RegisterAsync(
            new RegisterTenantRequest("Acme Ltd", VatStatus.NotRegistered, null, null), CancellationToken.None);

        var updated = await service.UpdateProfileAsync(
            registered.Id, new UpdateTenantProfileRequest("1 Main Road", empty, empty, empty), CancellationToken.None);

        Assert.NotNull(updated);
    }
}
