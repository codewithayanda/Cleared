using Cleared.Domain.Tenancy;

namespace Cleared.Domain.Tests.Tenancy;

public class CustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_EmptyTenantId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), Guid.Empty, "Jane Doe", Now));
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), TenantId, " ", Now));
    }

    [Fact]
    public void Create_ValidInput_SetsAllFields()
    {
        var customer = Customer.Create(
            Guid.NewGuid(), TenantId, "Jane Doe", Now, vatNumber: "4123456789", email: "jane@example.co.za");

        Assert.Equal(TenantId, customer.TenantId);
        Assert.Equal("Jane Doe", customer.Name);
        Assert.Equal("4123456789", customer.VatNumber);
        Assert.Equal("jane@example.co.za", customer.Email);
        Assert.Equal(Now, customer.CreatedAt);
    }

    [Fact]
    public void UpdateDetails_BlankName_Throws()
    {
        var customer = Customer.Create(Guid.NewGuid(), TenantId, "Jane Doe", Now);

        Assert.Throws<ArgumentException>(() => customer.UpdateDetails(""));
    }

    [Fact]
    public void UpdateDetails_ValidInput_UpdatesFields()
    {
        var customer = Customer.Create(Guid.NewGuid(), TenantId, "Jane Doe", Now);

        customer.UpdateDetails("Jane Smith", email: "jane.smith@example.co.za");

        Assert.Equal("Jane Smith", customer.Name);
        Assert.Equal("jane.smith@example.co.za", customer.Email);
        Assert.Null(customer.VatNumber);
    }
}
