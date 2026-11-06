using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Customers;

namespace Cleared.Integration.Tests;

[Collection(ApiCollection.Name)]
public class CustomerEditTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task A_customer_can_be_fetched_by_its_id()
    {
        var owner = await factory.CreateTenantAsync();
        var created = await owner.Client.CreateCustomerAsync("Beta Co");

        var fetched = await owner.Client.GetFromJsonAsync<CustomerResponse>(
            $"api/v1/customers/{created.Id}", ApiJson.Options);

        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task A_customer_that_does_not_exist_is_not_found_to_read_or_to_change()
    {
        var owner = await factory.CreateTenantAsync();
        var missing = $"api/v1/customers/{Guid.NewGuid()}";

        using var read = await owner.Client.GetAsync(missing);
        using var change = await owner.Client.PutAsJsonAsync(
            missing, new UpdateCustomerRequest("Nobody", null, null, null), ApiJson.Options);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
    }

    [Fact]
    public async Task Changing_a_customer_saves_every_detail_and_the_list_and_the_customer_show_it()
    {
        var owner = await factory.CreateTenantAsync();
        var created = await owner.Client.CreateCustomerAsync("Beta Co");
        var request = new UpdateCustomerRequest("Gamma Ltd", "4111111111", "accounts@gamma.test", "9 High Street, Durban");

        using var response = await owner.Client.PutAsJsonAsync($"api/v1/customers/{created.Id}", request, ApiJson.Options);
        var changed = await response.Content.ReadFromJsonAsync<CustomerResponse>(ApiJson.Options);

        var expected = new CustomerResponse(
            created.Id, owner.TenantId, "Gamma Ltd", "4111111111", "accounts@gamma.test", "9 High Street, Durban");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, changed);
        Assert.Equal(expected, await owner.Client.GetFromJsonAsync<CustomerResponse>(
            $"api/v1/customers/{created.Id}", ApiJson.Options));
        var listed = await owner.Client.GetFromJsonAsync<List<CustomerResponse>>("api/v1/customers", ApiJson.Options);
        Assert.Equal(expected, Assert.Single(listed!));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task A_customer_cannot_be_left_without_a_name_and_nothing_changes(string name)
    {
        var owner = await factory.CreateTenantAsync();
        var created = await owner.Client.CreateCustomerAsync("Beta Co");

        using var response = await owner.Client.PutAsJsonAsync(
            $"api/v1/customers/{created.Id}", new UpdateCustomerRequest(name, null, null, null), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(created, await owner.Client.GetFromJsonAsync<CustomerResponse>(
            $"api/v1/customers/{created.Id}", ApiJson.Options));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("jane@example")]
    public async Task An_email_that_cannot_be_real_is_refused_and_nothing_changes(string email)
    {
        var owner = await factory.CreateTenantAsync();
        var created = await owner.Client.CreateCustomerAsync("Beta Co");

        using var response = await owner.Client.PutAsJsonAsync(
            $"api/v1/customers/{created.Id}", new UpdateCustomerRequest("Beta Co", null, email, null), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(created, await owner.Client.GetFromJsonAsync<CustomerResponse>(
            $"api/v1/customers/{created.Id}", ApiJson.Options));
    }
}
