using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Cleared.Domain.Tenancy;
using Npgsql;

namespace Cleared.Integration.Tests;

// A clock the test moves by hand.
public sealed class MutableClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime.AddHours(2));
}

// What a browser holds after signing in: the access token from the body and the refresh cookie.
public sealed record Session(string Email, string Password, string AccessToken, string RefreshToken);

public static class SessionTestSupport
{
    public const string CookieName = "cleared_refresh";
    public const string StrongPassword = "Integration-Tests-1!";

    // Every way a refresh can fail gets exactly this answer, so it reveals nothing.
    public const string SessionEndedBody = "{\"title\":\"Your session has ended. Sign in again.\"}";

    public static async Task<Session> RegisterAsync(HttpClient client)
    {
        var email = $"session-{Guid.NewGuid():N}@example.test";

        using var response = await client.PostAsJsonAsync(
            "api/v1/auth/register",
            new RegisterRequest(
                new RegisterTenantRequest($"Session Co {Guid.NewGuid():N}", VatStatus.NotRegistered, null, null),
                email,
                StrongPassword),
            ApiJson.Options);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!;

        return new Session(email, StrongPassword, auth.AccessToken, RefreshCookieFrom(response)!);
    }

    // For tests that expect the answer might be a refusal.
    public static Task<HttpResponseMessage> TryRegisterAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "api/v1/auth/register",
            new RegisterRequest(
                new RegisterTenantRequest($"Session Co {Guid.NewGuid():N}", VatStatus.NotRegistered, null, null),
                $"session-{Guid.NewGuid():N}@example.test",
                StrongPassword),
            ApiJson.Options);

    // A browser that still holds a cookie sends it with the sign-in too.
    public static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, Session session, string? presentedCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(session.Email, session.Password), options: ApiJson.Options),
        };

        if (presentedCookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={presentedCookie}");
        }

        return await client.SendAsync(request);
    }

    public static HttpRequestMessage Refresh(string? cookie, string? fetchSite = null) =>
        WithCookie(new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh"), cookie, fetchSite);

    public static HttpRequestMessage Logout(string? cookie) =>
        WithCookie(new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout"), cookie, null);

    public static string? SetCookieLine(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;

    // The cookie's value, or null when the response cleared it or never set one.
    public static string? RefreshCookieFrom(HttpResponseMessage response)
    {
        var line = SetCookieLine(response);
        if (line is null)
        {
            return null;
        }

        var value = line[(CookieName.Length + 1)..].Split(';')[0];

        return value.Length == 0 ? null : value;
    }

    [SuppressMessage(
        "Security", "CA2100", Justification = "Tests pass literal statements and bind every value as a parameter.")]
    public static async Task<int> ExecuteAsync(
        ClearedApiFactory factory, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    [SuppressMessage(
        "Security", "CA2100", Justification = "Tests pass literal statements and bind every value as a parameter.")]
    public static async Task<object?> ScalarAsync(
        ClearedApiFactory factory, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteScalarAsync();
    }

    private static HttpRequestMessage WithCookie(HttpRequestMessage request, string? cookie, string? fetchSite)
    {
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        return request;
    }
}
