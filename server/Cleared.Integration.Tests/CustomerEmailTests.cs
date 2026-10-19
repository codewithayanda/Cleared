using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Customers;

namespace Cleared.Integration.Tests;

// An address that cannot be one is the client's mistake: a 400 and nothing saved, never a 500.
[Collection(ApiCollection.Name)]
public class CustomerEmailTests(ClearedApiFactory factory)
{
    [Theory]
    [InlineData("not-an-email")]
    [InlineData("jane@localhost")]
    [InlineData("jane@@example.test")]
    [InlineData("jane doe@example.test")]
    [InlineData("")]
    public async Task An_invalid_email_is_a_400_and_nothing_is_saved(string email)
    {
        var owner = await factory.CreateTenantAsync();

        var response = await owner.Client.PostAsJsonAsync(
            "api/v1/customers", new CreateCustomerRequest("Typo Customer", null, email, null), ApiJson.Options);
        var customers = await owner.Client.GetFromJsonAsync<List<CustomerResponse>>("api/v1/customers", ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(customers!);
    }

    [Fact]
    public async Task An_email_too_long_to_be_real_is_a_400_not_a_500()
    {
        var owner = await factory.CreateTenantAsync();
        var email = new string('a', 400) + "@example.test";

        var response = await owner.Client.PostAsJsonAsync(
            "api/v1/customers", new CreateCustomerRequest("Typo Customer", null, email, null), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("jane@example.co.za")]
    [InlineData("jane+invoices@example.co.za")]
    [InlineData("o'brien@example.co.za")]
    public async Task A_real_email_is_saved_exactly_as_typed(string email)
    {
        var owner = await factory.CreateTenantAsync();

        var created = await owner.Client.PostJsonAsync<CustomerResponse>(
            "api/v1/customers", new CreateCustomerRequest("Real Customer", null, email, null));

        Assert.Equal(email, created.Email);
    }

    [Fact]
    public async Task A_customer_does_not_need_an_email()
    {
        var owner = await factory.CreateTenantAsync();

        var created = await owner.Client.PostJsonAsync<CustomerResponse>(
            "api/v1/customers", new CreateCustomerRequest("No Email Customer", null, null, null));

        Assert.Null(created.Email);
    }
}
