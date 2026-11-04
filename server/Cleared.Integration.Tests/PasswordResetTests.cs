using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cleared.Integration.Tests;

// Forgetting a password is the one way in that needs no password, so each test here is about who it
// lets in and what it tells the wrong person.
[Collection(ApiCollection.Name)]
public class PasswordResetTests(ClearedApiFactory factory)
{
    private const string NewPassword = "Brand-New-Password-2!";
    private const string OtherPassword = "Another-Password-3!";
    private const string Wrong = "Wrong-Password-1";
    private const string LinkProblem = "This link is invalid or has expired. Ask for a new one.";

    [Fact]
    public async Task Asking_for_a_link_emails_the_address_and_keeps_only_a_hash_of_the_secret()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var response = await ForgotAsync(client, session.Email);
        var email = Assert.Single(await factory.SentToAsync(session.Email));
        var token = EmailTestSupport.TokenIn(email);
        var stored = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM password_reset_tokens WHERE token_hash = @hash AND length(token_hash) = 32",
            ("hash", SHA256.HashData(Encoding.ASCII.GetBytes(token))));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Reset your Cleared password", email.Subject);
        Assert.Contains($"http://localhost:4200/reset-password#{token}", email.Body, StringComparison.Ordinal);
        Assert.Contains("within 30 minutes", email.Body, StringComparison.Ordinal);
        Assert.Equal(43, token.Length);
        Assert.Equal(1L, stored);
    }

    [Fact]
    public async Task An_email_with_no_account_gets_exactly_the_same_answer_and_no_message()
    {
        using var client = factory.CreateAnonymousClient();
        var known = await SessionTestSupport.RegisterAsync(client);
        var unknown = $"nobody-{Guid.NewGuid():N}@example.test";

        using var forKnown = await ForgotAsync(client, known.Email);
        using var forUnknown = await ForgotAsync(client, unknown);

        Assert.Equal(forKnown.StatusCode, forUnknown.StatusCode);
        Assert.Equal(await forKnown.Content.ReadAsStringAsync(), await forUnknown.Content.ReadAsStringAsync());
        Assert.Equal(Describe(forKnown), Describe(forUnknown));
        Assert.Single(await factory.SentToAsync(known.Email));
        Assert.Empty(await factory.SentToAsync(unknown));
    }

    [Fact]
    public async Task The_link_sets_a_new_password_once_and_the_old_one_stops_working()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var token = await LinkForAsync(client, session.Email);

        using var reset = await ResetAsync(client, token, NewPassword);
        var withNew = await SignInAsync(client, session.Email, NewPassword);
        var withOld = await SignInAsync(client, session.Email, session.Password);
        using var again = await ResetAsync(client, token, OtherPassword);
        var problem = await ProblemAsync(again);
        var withSecond = await SignInAsync(client, session.Email, OtherPassword);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNew);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(LinkProblem, Assert.Single(problem.Errors["token"]));
        Assert.Equal(HttpStatusCode.Unauthorized, withSecond);
    }

    [Fact]
    public async Task A_reset_ends_every_session_at_once_and_signs_nobody_in()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        using var secondSignIn = await SessionTestSupport.LoginAsync(client, session);
        var secondCookie = SessionTestSupport.RefreshCookieFrom(secondSignIn)!;
        using var control = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));
        var firstCookie = SessionTestSupport.RefreshCookieFrom(control)!;
        var token = await LinkForAsync(client, session.Email);

        using var reset = await ResetAsync(client, token, NewPassword);
        var stillLive = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM refresh_tokens r JOIN \"AspNetUsers\" u ON u.id = r.user_id WHERE u.normalized_email = @email AND r.revoked_at IS NULL",
            ("email", session.Email.ToUpperInvariant()));
        using var firstAfter = await client.SendAsync(SessionTestSupport.Refresh(firstCookie));
        using var secondAfter = await client.SendAsync(SessionTestSupport.Refresh(secondCookie));

        Assert.Equal(HttpStatusCode.OK, control.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Empty(await reset.Content.ReadAsStringAsync());
        Assert.Null(SessionTestSupport.SetCookieLine(reset));
        Assert.Equal(0L, stillLive);
        Assert.Equal(HttpStatusCode.Unauthorized, firstAfter.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAfter.StatusCode);
    }

    [Fact]
    public async Task A_reset_lifts_a_sign_in_lock_so_the_owner_can_get_back_in()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await SignInAsync(client, session.Email, Wrong);
        }

        var whileLocked = await SignInAsync(client, session.Email, session.Password);
        var token = await LinkForAsync(client, session.Email);
        using var reset = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, whileLocked);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(client, session.Email, NewPassword));
    }

    [Fact]
    public async Task A_reset_emails_the_owner_that_the_password_was_changed_and_the_email_holds_no_secret()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var token = await LinkForAsync(client, session.Email);

        using var reset = await ResetAsync(client, token, NewPassword);
        var emails = await factory.SentToAsync(session.Email);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(2, emails.Count);
        Assert.Equal("Your Cleared password was changed", emails[1].Subject);
        Assert.Contains("http://localhost:4200/forgot-password", emails[1].Body, StringComparison.Ordinal);
        Assert.DoesNotContain(token, emails[1].Body, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, emails[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_that_breaks_the_rules_says_so_and_leaves_the_link_ready_for_another_try()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var token = await LinkForAsync(client, session.Email);

        using var weak = await ResetAsync(client, token, "short");
        var problem = await ProblemAsync(weak);
        using var good = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains(problem.Errors["password"], message => message.Contains("10 characters", StringComparison.Ordinal));
        Assert.False(problem.Errors.ContainsKey("token"));
        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
    }

    [Fact]
    public async Task A_link_works_for_thirty_minutes_and_not_a_minute_longer()
    {
        var clock = new MutableClock();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        var first = await LinkForAsync(client, session.Email, host);
        clock.UtcNow = clock.UtcNow.AddMinutes(29);
        using var inTime = await ResetAsync(client, first, NewPassword);

        var second = await LinkForAsync(client, session.Email, host);
        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        using var late = await ResetAsync(client, second, OtherPassword);

        Assert.Equal(HttpStatusCode.NoContent, inTime.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [Fact]
    public async Task Asking_again_ends_the_earlier_link_and_leaves_exactly_one_live()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        var first = await LinkForAsync(client, session.Email);
        var second = await LinkForAsync(client, session.Email);
        var live = await LiveLinksAsync(session.Email);
        using var withFirst = await ResetAsync(client, first, NewPassword);
        using var withSecond = await ResetAsync(client, second, NewPassword);

        Assert.Equal(1L, live);
        Assert.Equal(HttpStatusCode.BadRequest, withFirst.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withSecond.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("not a token at all")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task A_link_nobody_issued_is_refused_the_same_way_whatever_its_shape(string token)
    {
        using var client = factory.CreateAnonymousClient();

        using var unknown = await ResetAsync(client, new string('A', 43), NewPassword);
        using var response = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(44)]
    [InlineData(10_000)]
    public async Task A_link_of_the_wrong_length_is_a_400_and_never_a_500(int length)
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await ResetAsync(client, new string('A', length), NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("jane doe@example.test")]
    public async Task An_email_that_cannot_be_real_is_a_400_next_to_the_email_box_and_sends_nothing(string email)
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await ForgotAsync(client, email);
        var problem = await ProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter a valid email address.", Assert.Single(problem.Errors["email"]));
        Assert.Empty(await factory.SentToAsync(email));
    }

    [Fact]
    public async Task An_email_too_long_to_be_real_is_a_400_and_never_a_500()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await ForgotAsync(client, new string('a', 300) + "@example.test");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ten_uses_of_one_link_at_once_change_the_password_exactly_once()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var token = await LinkForAsync(client, session.Email);

        var responses = await ConcurrentCalls.FireAsync(10, () => ResetAsync(client, token, NewPassword));
        var emails = await factory.SentToAsync(session.Email);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.Single(emails, message => message.Subject == "Your Cleared password was changed");
        Assert.Equal(HttpStatusCode.OK, await SignInAsync(client, session.Email, NewPassword));
    }

    [Fact]
    public async Task Fifty_requests_for_one_email_at_once_send_exactly_three_emails_and_leave_one_live_link()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        var responses = await ConcurrentCalls.FireAsync(50, () => ForgotAsync(client, session.Email));
        var emails = await factory.SentToAsync(session.Email);
        var refused = responses.Where(response => response.StatusCode == HttpStatusCode.TooManyRequests).ToList();

        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(47, refused.Count);
        Assert.All(refused, response => Assert.InRange(RetryAfter(response), 3590, 3600));
        Assert.Equal(3, emails.Count);
        Assert.Equal(1L, await LiveLinksAsync(session.Email));
    }

    [Fact]
    public async Task An_email_with_no_account_is_capped_exactly_like_one_with_an_account()
    {
        using var client = factory.CreateAnonymousClient();
        var known = await SessionTestSupport.RegisterAsync(client);
        var unknown = $"nobody-{Guid.NewGuid():N}@example.test";

        var forKnown = await FourRequestsAsync(client, known.Email);
        var forUnknown = await FourRequestsAsync(client, unknown);

        Assert.Equal(forKnown, forUnknown);
        Assert.Equal(
            [HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests],
            forKnown);
    }

    [Fact]
    public async Task The_cap_comes_back_an_hour_after_the_first_request()
    {
        var clock = new MutableClock();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        var beforeTheHour = await FourRequestsAsync(client, session.Email);
        clock.UtcNow = clock.UtcNow.AddMinutes(61);
        using var afterTheHour = await ForgotAsync(client, session.Email);

        Assert.Equal(HttpStatusCode.TooManyRequests, beforeTheHour[3]);
        Assert.Equal(HttpStatusCode.Accepted, afterTheHour.StatusCode);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_live_link_for_one_person()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        await LinkForAsync(client, session.Email);
        var userId = await UserIdAsync(session.Email);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => SessionTestSupport.ExecuteAsync(
            factory,
            "INSERT INTO password_reset_tokens (id, user_id, token_hash, created_at, expires_at) VALUES (@id, @user, @hash, @now, @later)",
            ("id", Guid.NewGuid()),
            ("user", userId),
            ("hash", RandomNumberGenerator.GetBytes(32)),
            ("now", DateTimeOffset.UtcNow),
            ("later", DateTimeOffset.UtcNow.AddMinutes(30))));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
    }

    [Fact]
    public async Task Finished_links_and_counts_nobody_needs_any_more_are_cleared_as_new_ones_are_made()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        var userId = await UserIdAsync(session.Email);
        var oldCount = RandomNumberGenerator.GetBytes(32);
        var oldLink = RandomNumberGenerator.GetBytes(32);
        var longAgo = DateTimeOffset.UtcNow.AddDays(-3);
        await SessionTestSupport.ExecuteAsync(
            factory,
            "INSERT INTO password_reset_throttles (email_hash, window_start, requests) VALUES (@hash, @then, 3)",
            ("hash", oldCount),
            ("then", longAgo));
        await SessionTestSupport.ExecuteAsync(
            factory,
            "INSERT INTO password_reset_tokens (id, user_id, token_hash, created_at, expires_at, consumed_at) VALUES (@id, @user, @hash, @then, @then, @then)",
            ("id", Guid.NewGuid()),
            ("user", userId),
            ("hash", oldLink),
            ("then", longAgo));

        using var response = await ForgotAsync(client, $"new-{Guid.NewGuid():N}@example.test");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            0L,
            await SessionTestSupport.ScalarAsync(
                factory, "SELECT count(*) FROM password_reset_throttles WHERE email_hash = @hash", ("hash", oldCount)));
        Assert.Equal(
            0L,
            await SessionTestSupport.ScalarAsync(
                factory, "SELECT count(*) FROM password_reset_tokens WHERE token_hash = @hash", ("hash", oldLink)));
    }

    [Fact]
    public async Task Both_endpoints_refuse_cross_site_requests_and_answer_with_no_store()
    {
        using var client = factory.CreateAnonymousClient();

        using var crossSiteForgot = await SendWithAsync(client, "forgot-password", new ForgotPasswordRequest("a@b.co.za"), "cross-site");
        using var crossSiteReset = await SendWithAsync(client, "reset-password", new ResetPasswordRequest(new string('A', 43), NewPassword), "cross-site");
        using var sameSiteForgot = await SendWithAsync(client, "forgot-password", new ForgotPasswordRequest("a@b.co.za"), "same-origin");
        using var sameSiteReset = await SendWithAsync(client, "reset-password", new ResetPasswordRequest(new string('A', 43), NewPassword), "same-origin");

        Assert.Equal(HttpStatusCode.Forbidden, crossSiteForgot.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossSiteReset.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, sameSiteForgot.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sameSiteReset.StatusCode);
        Assert.True(sameSiteForgot.Headers.CacheControl?.NoStore);
        Assert.True(sameSiteReset.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("PasswordReset:TokenLifetime", "00:00:00", "PasswordReset")]
    [InlineData("PasswordReset:MaxRequestsPerWindow", "0", "PasswordReset")]
    [InlineData("PasswordReset:Window", "00:00:00", "PasswordReset")]
    [InlineData("App:PublicUrl", "not a url", "App:PublicUrl")]
    [InlineData("App:PublicUrl", "", "App:PublicUrl")]
    [InlineData("App:PublicUrl", "ftp://example.test", "App:PublicUrl")]
    public void Impossible_settings_stop_the_API_from_starting(string setting, string value, string named)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains(named, StartupFailure.Messages(failure), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("api/v1/auth/forgot-password", new ForgotPasswordRequest(email), ApiJson.Options);

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string token, string password) =>
        client.PostAsJsonAsync("api/v1/auth/reset-password", new ResetPasswordRequest(token, password), ApiJson.Options);

    private static async Task<HttpResponseMessage> SendWithAsync(
        HttpClient client, string endpoint, object body, string fetchSite)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/auth/{endpoint}")
        {
            Content = JsonContent.Create(body, options: ApiJson.Options),
        };
        request.Headers.Add("Sec-Fetch-Site", fetchSite);

        return await client.SendAsync(request);
    }

    // Asks for a link the way a person does, then reads it out of the email that was sent.
    private async Task<string> LinkForAsync(
        HttpClient client, string email, WebApplicationFactory<Program>? host = null)
    {
        using var response = await ForgotAsync(client, email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return EmailTestSupport.TokenIn((await factory.SentToAsync(email, host))[^1]);
    }

    private static async Task<ValidationProblemDetails> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;

    private static async Task<HttpStatusCode> SignInAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest(email, password), ApiJson.Options);

        return response.StatusCode;
    }

    private static async Task<List<HttpStatusCode>> FourRequestsAsync(HttpClient client, string email)
    {
        var statuses = new List<HttpStatusCode>();

        for (var request = 0; request < 4; request++)
        {
            using var response = await ForgotAsync(client, email);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private async Task<long> LiveLinksAsync(string email)
    {
        var count = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM password_reset_tokens t JOIN \"AspNetUsers\" u ON u.id = t.user_id WHERE u.normalized_email = @email AND t.consumed_at IS NULL",
            ("email", email.ToUpperInvariant()));

        return (long)count!;
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        var id = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT id FROM \"AspNetUsers\" WHERE normalized_email = @email",
            ("email", email.ToUpperInvariant()));

        return (Guid)id!;
    }

    // Every header but the clock, so two answers can be compared for anything that tells them apart.
    private static string Describe(HttpResponseMessage response) =>
        string.Join(
            "; ",
            response.Headers.Concat(response.Content.Headers)
                .Where(header => header.Key != "Date")
                .OrderBy(header => header.Key, StringComparer.Ordinal)
                .Select(header => $"{header.Key}={string.Join(',', header.Value)}"));

    private static int RetryAfter(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
}
