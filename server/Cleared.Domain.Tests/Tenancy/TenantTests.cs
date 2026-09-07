using Cleared.Domain.Tenancy;

namespace Cleared.Domain.Tests.Tenancy;

public class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_Registered_RequiresVatNumber()
    {
        Assert.Throws<ArgumentException>(() =>
            Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.Registered, vatNumber: null, Now));
    }

    [Fact]
    public void Register_NotRegistered_RejectsVatNumber()
    {
        Assert.Throws<ArgumentException>(() =>
            Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.NotRegistered, "4123456789", Now));
    }

    [Fact]
    public void Register_BlankCompanyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Tenant.Register(Guid.NewGuid(), "  ", VatStatus.NotRegistered, vatNumber: null, Now));
    }

    [Fact]
    public void Register_NotRegisteredTenant_CannotIssueTaxInvoices()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.NotRegistered, null, Now);

        Assert.False(tenant.CanIssueTaxInvoices());
    }

    [Fact]
    public void Register_RegisteredTenant_CanIssueTaxInvoices()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.Registered, "4123456789", Now);

        Assert.True(tenant.CanIssueTaxInvoices());
    }

    [Fact]
    public void RegisterForVat_NotRegisteredTenant_BecomesRegistered()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.NotRegistered, null, Now);

        tenant.RegisterForVat("4123456789");

        Assert.Equal(VatStatus.Registered, tenant.VatStatus);
        Assert.Equal("4123456789", tenant.VatNumber);
        Assert.True(tenant.CanIssueTaxInvoices());
    }

    [Fact]
    public void RegisterForVat_AlreadyRegistered_Throws()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.Registered, "4123456789", Now);

        Assert.Throws<InvalidOperationException>(() => tenant.RegisterForVat("4999999999"));
    }

    [Fact]
    public void RegisterForVat_BlankVatNumber_Throws()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.NotRegistered, null, Now);

        Assert.Throws<ArgumentException>(() => tenant.RegisterForVat(""));
    }

    [Fact]
    public void Rename_UpdatesCompanyAndTradingName()
    {
        var tenant = Tenant.Register(Guid.NewGuid(), "Acme (Pty) Ltd", VatStatus.NotRegistered, null, Now);

        tenant.Rename("Acme Holdings (Pty) Ltd", "Acme");

        Assert.Equal("Acme Holdings (Pty) Ltd", tenant.CompanyName);
        Assert.Equal("Acme", tenant.TradingName);
    }
}
