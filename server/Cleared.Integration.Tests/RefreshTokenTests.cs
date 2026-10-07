using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cleared.Integration.Tests;

// The refresh token is what keeps a person signed in past the 15-minute access token, so every way
// it can go wrong is tested: copies, expiry, ended sessions, races and malformed input.
[Collection(ApiCollection.Name)]
public class RefreshTokenTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task Signing_in_sets_an_http_only_refresh_cookie_and_keeps_the_token_out_of_the_body()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var login = await SessionTestSupport.LoginAsync(client, session);
        var cookie = SessionTestSupport.SetCookieLine(login)!;
        var body = await login.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SessionTestSupport.RefreshCookieFrom(login)!, body);
        Assert.True(login.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Registering_also_starts_a_session_that_can_be_refreshed()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var refreshed = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        var asPlainText = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM refresh_tokens WHERE encode(token_hash, 'escape') = @token",
            ("token", session.RefreshToken));

        Assert.Equal(1L, await StoredRowsAsync(session.RefreshToken));
        Assert.Equal(0L, (long)asPlainText!);
    }

    [Fact]
    public async Task Finished_sessions_are_kept_for_a_day_and_cleared_at_the_next_sign_in_after_that()
    {
        var clock = new MutableClock();
        using var host = WithClock(clock);
        using var client = host.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var start = clock.UtcNow;

        // Ten hours past the 30-day limit the session is over, but a late replay must still be recognised.
        clock.UtcNow = start.AddDays(30).AddHours(10);
        using var firstSignIn = await SessionTestSupport.LoginAsync(client, session);
        var keptAtFirst = await StoredRowsAsync(session.RefreshToken);

        // A day and ten hours past it, the next sign-in clears the old rows out.
        clock.UtcNow = start.AddDays(31).AddHours(10);
        using var secondSignIn = await SessionTestSupport.LoginAsync(client, session);
        var keptAtSecond = await StoredRowsAsync(session.RefreshToken);

        Assert.Equal(1L, keptAtFirst);
        Assert.Equal(0L, keptAtSecond);
    }

    [Fact]
    public async Task Refreshing_gives_an_access_token_for_the_same_tenant_and_a_different_cookie()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var refreshed = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));
        var auth = await refreshed.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(session.RefreshToken, SessionTestSupport.RefreshCookieFrom(refreshed));
        Assert.Equal((await TenantAsync(client, session.AccessToken)).Id, (await TenantAsync(client, auth!.AccessToken)).Id);
    }

    [Fact]
    public async Task A_used_token_is_dead_and_reusing_it_revokes_the_whole_family()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var first = session.RefreshToken;

        using var secondResponse = await client.SendAsync(SessionTestSupport.Refresh(first));
        var second = SessionTestSupport.RefreshCookieFrom(secondResponse)!;
        using var thirdResponse = await client.SendAsync(SessionTestSupport.Refresh(second));
        var third = SessionTestSupport.RefreshCookieFrom(thirdResponse)!;

        // A copy of the first token turns up after it was already swapped twice.
        using var replay = await client.SendAsync(SessionTestSupport.Refresh(first));
        // The newest token was fine a moment ago. The copy proved the session was leaked, so it goes too.
        using var newest = await client.SendAsync(SessionTestSupport.Refresh(third));

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, newest.StatusCode);
    }

    [Fact]
    public async Task A_missing_malformed_or_unknown_token_gets_one_identical_401_and_clears_the_cookie()
    {
        using var client = factory.CreateAnonymousClient();
        string?[] cookies =
        [
            null,
            string.Empty,
            "not-a-token",
            new string('A', 43),
            Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
        ];

        foreach (var cookie in cookies)
        {
            using var response = await client.SendAsync(SessionTestSupport.Refresh(cookie));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(SessionTestSupport.SessionEndedBody, await response.Content.ReadAsStringAsync());
            Assert.Contains("1970", SessionTestSupport.SetCookieLine(response));
        }
    }

    [Fact]
    public async Task A_session_that_sits_idle_for_seven_days_is_over()
    {
        var clock = new MutableClock();
        using var host = WithClock(clock);
        using var client = host.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        clock.UtcNow = clock.UtcNow.AddDays(7).AddSeconds(1);
        using var late = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
    }

    [Fact]
    public async Task A_session_slides_while_it_is_used_and_still_ends_at_the_absolute_limit()
    {
        var clock = new MutableClock();
        using var host = WithClock(clock);
        using var client = host.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var start = clock.UtcNow;
        var cookie = session.RefreshToken;

        // Six days between uses is inside the 7-day idle window, and each use moves the window on.
        foreach (var day in new[] { 6, 12, 18, 24 })
        {
            clock.UtcNow = start.AddDays(day);
            using var response = await client.SendAsync(SessionTestSupport.Refresh(cookie));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            cookie = SessionTestSupport.RefreshCookieFrom(response)!;
        }

        // Day 30 is the absolute limit set at sign-in, however recently the session was used.
        clock.UtcNow = start.AddDays(30).AddSeconds(1);
        using var late = await client.SendAsync(SessionTestSupport.Refresh(cookie));

        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
    }

    [Fact]
    public async Task A_locked_out_user_cannot_refresh_but_keeps_the_session()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var email = session.Email.ToUpperInvariant();

        await SessionTestSupport.ExecuteAsync(
            factory,
            "UPDATE \"AspNetUsers\" SET lockout_end = @end WHERE normalized_email = @email",
            ("end", DateTimeOffset.UtcNow.AddHours(1)),
            ("email", email));
        using var refused = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));

        // The lockout ends. The same token still works, because being locked out is not theft.
        await SessionTestSupport.ExecuteAsync(
            factory, "UPDATE \"AspNetUsers\" SET lockout_end = NULL WHERE normalized_email = @email", ("email", email));
        using var allowed = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Changing_the_security_stamp_ends_every_session_of_the_user()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        using var otherBrowser = await SessionTestSupport.LoginAsync(client, session);
        var otherCookie = SessionTestSupport.RefreshCookieFrom(otherBrowser)!;

        // Identity changes the stamp on a password change or reset.
        await SessionTestSupport.ExecuteAsync(
            factory,
            "UPDATE \"AspNetUsers\" SET security_stamp = @stamp WHERE normalized_email = @email",
            ("stamp", Guid.NewGuid().ToString("N")),
            ("email", session.Email.ToUpperInvariant()));

        using var first = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));
        using var second = await client.SendAsync(SessionTestSupport.Refresh(otherCookie));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Twelve_refreshes_with_one_token_at_once_let_exactly_one_through_and_then_end_the_session()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        // The token's row is held while the callers start, so each one reads it as unused and then queues
        // to swap it. Without the hold the first caller can finish before the others reach the database,
        // and the race this test is about never happens.
        const int Callers = 12;
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var hold = await connection.BeginTransactionAsync();
        await using (var holdRow = new NpgsqlCommand(
            "SELECT 1 FROM refresh_tokens WHERE upper(encode(token_hash, 'hex')) = @hash FOR UPDATE", connection, hold))
        {
            holdRow.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(session.RefreshToken))));
            Assert.NotNull(await holdRow.ExecuteScalarAsync());
        }

        var race = ConcurrentCalls.FireAsync(
            Callers, () => client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken)));
        await WaitUntilQueuedAsync(Callers);
        await hold.RollbackAsync();
        var responses = await race;

        var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(
            responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));

        // The extra copies looked like theft, so the winner's new token was revoked with the family.
        using var afterwards = await client.SendAsync(
            SessionTestSupport.Refresh(SessionTestSupport.RefreshCookieFrom(winner)));
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Theory]
    [InlineData("cross-site", HttpStatusCode.Forbidden)]
    [InlineData("same-origin", HttpStatusCode.OK)]
    [InlineData("same-site", HttpStatusCode.OK)]
    [InlineData("none", HttpStatusCode.OK)]
    public async Task Only_a_cross_site_refresh_is_refused(string fetchSite, HttpStatusCode expected)
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var response = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken, fetchSite));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_cross_site_request_does_not_use_up_the_token()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var refused = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken, "cross-site"));
        using var genuine = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken, "same-origin"));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, genuine.StatusCode);
    }

    // Waits until the database reports this many token swaps stuck behind the held row.
    private async Task WaitUntilQueuedAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var queued = (long)(await SessionTestSupport.ScalarAsync(
                factory,
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE 'UPDATE refresh_tokens%'"))!;

            if (queued >= expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Fewer than {expected} refreshes reached the database within 15 seconds.");
    }

    // How many rows hold the SHA-256 of this token. The token itself is never stored.
    private async Task<long> StoredRowsAsync(string token)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

        return (long)(await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM refresh_tokens WHERE upper(encode(token_hash, 'hex')) = @hash",
            ("hash", hash)))!;
    }

    private WebApplicationFactory<Program> WithClock(MutableClock clock) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));

    private static async Task<TenantResponse> TenantAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/tenants/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TenantResponse>(ApiJson.Options))!;
    }
}
