using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cleared.Application.Auth;
using Cleared.Application.Customers;
using Cleared.Application.Tenants;
using Cleared.Domain.Tenancy;
using Cleared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

[Collection(ApiCollection.Name)]
public class RegistrationTests(ClearedApiFactory factory)
{
    private const string Password = "Integration-Tests-1!";

    [Fact]
    public async Task Registering_creates_the_company_and_signs_in_its_owner()
    {
        using var client = factory.CreateAnonymousClient();
        var company = $"Registration Co {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", Registration(company, NewEmail(), Password), ApiJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenant = await client.GetFromJsonAsync<TenantResponse>("/api/v1/tenants/me", ApiJson.Options);
        Assert.Equal(company, tenant!.CompanyName);
    }

    [Fact]
    public async Task Naming_someone_elses_tenant_id_does_not_put_you_in_their_company()
    {
        var victim = await factory.CreateTenantAsync();
        var created = await victim.Client.PostAsJsonAsync(
            "/api/v1/customers", new CreateCustomerRequest("Victim Customer", null, null, null), ApiJson.Options);
        created.EnsureSuccessStatusCode();

        using var attacker = factory.CreateAnonymousClient();
        var response = await attacker.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                tenantId = victim.TenantId,
                company = new { companyName = $"Attacker Co {Guid.NewGuid():N}", vatStatus = "NotRegistered" },
                email = NewEmail(),
                password = Password,
            },
            ApiJson.Options);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!;
        attacker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var attackerTenant = await attacker.GetFromJsonAsync<TenantResponse>("/api/v1/tenants/me", ApiJson.Options);
        var attackerCustomers = await attacker.GetFromJsonAsync<List<CustomerResponse>>(
            "/api/v1/customers", ApiJson.Options);

        Assert.NotEqual(victim.TenantId, attackerTenant!.Id);
        Assert.Empty(attackerCustomers!);
    }

    [Fact]
    public async Task A_request_without_a_company_is_rejected()
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { tenantId = Guid.NewGuid(), email = NewEmail(), password = Password },
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_anonymous_tenant_endpoint_no_longer_exists()
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/tenants",
            new RegisterTenantRequest("Sneaky Co", VatStatus.NotRegistered, null, null),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_email_is_rejected_and_leaves_no_company_behind()
    {
        var existing = await factory.CreateTenantAsync();
        using var client = factory.CreateAnonymousClient();
        var company = $"Orphan Co {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", Registration(company, existing.Email, Password), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await CompanyExistsAsync(company));
    }

    [Fact]
    public async Task A_weak_password_is_rejected_and_leaves_no_company_behind()
    {
        using var client = factory.CreateAnonymousClient();
        var company = $"Orphan Co {Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register", Registration(company, NewEmail(), "short"), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await CompanyExistsAsync(company));
    }

    private static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.test";

    private static RegisterRequest Registration(string company, string email, string password) =>
        new(new RegisterTenantRequest(company, VatStatus.NotRegistered, null, null), email, password);

    private async Task<bool> CompanyExistsAsync(string companyName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();

        return await dbContext.Tenants.AnyAsync(t => t.CompanyName == companyName);
    }
}
