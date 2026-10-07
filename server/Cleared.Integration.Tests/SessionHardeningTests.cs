using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Cleared.Application.Auth;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cleared.Integration.Tests;

// The smaller defences around the session: the key, the algorithm, response timing and the logs.
[Collection(ApiCollection.Name)]
public class SessionHardeningTests(ClearedApiFactory factory)
{
    [Fact]
    public void A_short_signing_key_stops_the_api_from_starting()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Jwt:SigningKey", "too-short"));

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("at least 32 bytes", StartupFailure.Messages(failure));
    }

    [Fact]
    public void Session_lifetimes_that_contradict_each_other_stop_the_api_from_starting()
    {
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Session:IdleLifetime", "10.00:00:00");
            builder.UseSetting("Session:AbsoluteLifetime", "5.00:00:00");
        });

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("Session:AbsoluteLifetime", StartupFailure.Messages(failure));
    }

    [Fact]
    public async Task A_token_signed_with_another_algorithm_or_none_is_rejected_even_with_the_right_key()
    {
        using var client = factory.CreateAnonymousClient();

        // Control: the expected algorithm gets past authentication. The tenant does not exist, so 404.
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, Signed(SecurityAlgorithms.HmacSha256)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, Signed(SecurityAlgorithms.HmacSha512)));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, Unsigned()));
    }

    [Fact]
    public async Task The_access_token_lasts_fifteen_minutes_and_every_one_has_its_own_id()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        using var again = await SessionTestSupport.LoginAsync(client, session);
        var secondToken = (await again.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!.AccessToken;
        var handler = new JsonWebTokenHandler();

        var first = handler.ReadJsonWebToken(session.AccessToken);
        var second = handler.ReadJsonWebToken(secondToken);

        Assert.Equal(TimeSpan.FromMinutes(15), first.ValidTo - first.ValidFrom);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEmpty(first.Id);
    }

    [Fact]
    public async Task A_sign_in_for_an_unknown_email_still_verifies_a_password_hash_like_a_wrong_password_does()
    {
        var hasher = new CountingPasswordHasher();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher);
        }));
        using var client = host.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        var before = hasher.Verifications;
        using var unknown = await client.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest("nobody@example.test", "Whatever-Password-1"), ApiJson.Options);
        var unknownCost = hasher.Verifications - before;

        before = hasher.Verifications;
        using var wrong = await client.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest(session.Email, "Wrong-Password-1"), ApiJson.Options);
        var wrongCost = hasher.Verifications - before;

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(1, unknownCost);
        Assert.Equal(wrongCost, unknownCost);
    }

    [Fact]
    public async Task Reuse_is_logged_as_a_warning_and_no_token_ever_reaches_a_log()
    {
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = host.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var refreshed = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));
        var next = SessionTestSupport.RefreshCookieFrom(refreshed)!;
        using var replay = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));
        using var logout = await client.SendAsync(SessionTestSupport.Logout(next));

        var warning = Assert.Single(
            logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("reuse", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("family", warning.Message);
        foreach (var secret in new[] { session.RefreshToken, next, session.AccessToken })
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/tenants/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    // A token the API would accept, if only the algorithm matched.
    private static string Signed(string algorithm)
    {
        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "Cleared",
            Audience = "Cleared",
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("tenant_id", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Owner"),
            ]),
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ClearedApiFactory.SigningKey)), algorithm),
        });
    }

    private static string Unsigned()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();

        return new JsonWebTokenHandler().CreateToken(
            $"{{\"sub\":\"{Guid.NewGuid()}\",\"tenant_id\":\"{Guid.NewGuid()}\",\"iss\":\"Cleared\",\"aud\":\"Cleared\",\"exp\":{expires}}}");
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
