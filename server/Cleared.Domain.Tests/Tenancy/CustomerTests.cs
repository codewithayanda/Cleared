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
            Guid.NewGuid(), TenantId, "Jane Doe", Now,
            vatNumber: "4123456789", email: "jane@example.co.za", address: "1 Main Road, Cape Town");

        Assert.Equal(TenantId, customer.TenantId);
        Assert.Equal("Jane Doe", customer.Name);
        Assert.Equal("4123456789", customer.VatNumber);
        Assert.Equal("jane@example.co.za", customer.Email);
        Assert.Equal("1 Main Road, Cape Town", customer.Address);
        Assert.Equal(Now, customer.CreatedAt);
    }

    [Fact]
    public void Create_NoAddress_LeavesAddressNull()
    {
        var customer = Customer.Create(Guid.NewGuid(), TenantId, "Jane Doe", Now);

        Assert.Null(customer.Address);
    }

    [Fact]
    public void Create_NoEmail_LeavesEmailNull()
    {
        var customer = Customer.Create(Guid.NewGuid(), TenantId, "Jane Doe", Now);

        Assert.Null(customer.Email);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("jane@example")]
    [InlineData("")]
    [InlineData(" jane@example.co.za")]
    public void Create_InvalidEmail_Throws(string email)
    {
        Assert.Throws<ArgumentException>(() => Customer.Create(Guid.NewGuid(), TenantId, "Jane Doe", Now, email: email));
    }

    [Fact]
    public void UpdateDetails_InvalidEmail_ThrowsAndChangesNothing()
    {
        var customer = Customer.Create(
            Guid.NewGuid(), TenantId, "Jane Doe", Now, email: "jane@example.co.za", address: "1 Main Road");

        Assert.Throws<ArgumentException>(() => customer.UpdateDetails("Jane Smith", email: "not-an-email"));

        Assert.Equal("Jane Doe", customer.Name);
        Assert.Equal("jane@example.co.za", customer.Email);
        Assert.Equal("1 Main Road", customer.Address);
    }

    [Fact]
    public void UpdateDetails_NoEmail_ClearsTheEmail()
    {
        var customer = Customer.Create(Guid.NewGuid(), TenantId, "Jane Doe", Now, email: "jane@example.co.za");

        customer.UpdateDetails("Jane Doe");

        Assert.Null(customer.Email);
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

        customer.UpdateDetails(
            "Jane Smith", email: "jane.smith@example.co.za", address: "2 Main Road, Cape Town");

        Assert.Equal("Jane Smith", customer.Name);
        Assert.Equal("jane.smith@example.co.za", customer.Email);
        Assert.Equal("2 Main Road, Cape Town", customer.Address);
        Assert.Null(customer.VatNumber);
    }
}
