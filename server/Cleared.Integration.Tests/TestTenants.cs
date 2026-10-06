using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Cleared.Domain.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cleared.Integration.Tests;

public sealed record TestTenant(Guid TenantId, string Email, string Password, HttpClient Client);

public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

public static class ClearedApiFactoryExtensions
{
    private const string Password = "Integration-Tests-1!";

    // Redirects stay off so an unexpected 3xx fails a test instead of being followed.
    public static HttpClient CreateAnonymousClient(this ClearedApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Registers through the real endpoint, so tests take the same path a user does.
    public static async Task<TestTenant> CreateTenantAsync(this ClearedApiFactory factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"owner-{suffix}@example.test";
        using var anonymous = factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(
                new RegisterTenantRequest($"Test Co {suffix}", VatStatus.NotRegistered, null, null), email, Password),
            ApiJson.Options);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!;

        var client = factory.CreateDefaultClient(new IdempotencyKeyHandler());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenant = (await client.GetFromJsonAsync<TenantResponse>("/api/v1/tenants/me", ApiJson.Options))!;

        return new TestTenant(tenant.Id, email, Password, client);
    }
}
