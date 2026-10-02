using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Auth;
using Cleared.Application.Customers;

namespace Cleared.Integration.Tests;

[Collection(ApiCollection.Name)]
public class ApiSmokeTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task Customers_endpoint_rejects_a_request_with_no_token()
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registered_owner_can_create_a_customer_and_list_it()
    {
        var tenant = await factory.CreateTenantAsync();

        var created = await tenant.Client.PostAsJsonAsync(
            "/api/v1/customers",
            new CreateCustomerRequest("Acme Ltd", null, "buyer@acme.test", "1 Main Road, Cape Town"),
            ApiJson.Options);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var customers = await tenant.Client.GetFromJsonAsync<List<CustomerResponse>>(
            "/api/v1/customers", ApiJson.Options);

        var customer = Assert.Single(customers!);
        Assert.Equal("Acme Ltd", customer.Name);
        Assert.Equal(tenant.TenantId, customer.TenantId);
    }

    [Fact]
    public async Task Login_with_the_registered_credentials_returns_a_token()
    {
        var tenant = await factory.CreateTenantAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(tenant.Email, tenant.Password), ApiJson.Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
    }

    [Fact]
    public async Task Login_with_the_wrong_password_is_rejected()
    {
        var tenant = await factory.CreateTenantAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(tenant.Email, "not-the-password-1"), ApiJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
