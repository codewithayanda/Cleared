using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// Confirming an email is the one thing a link can do to an account, so each test here is about who a link
// lets through, what an unconfirmed account may not do, and what a mail scanner cannot do.
[Collection(ApiCollection.Name)]
public class EmailVerificationTests(ClearedApiFactory factory)
{
    private const string _linkProblem = "This link is invalid or has expired. Ask for a new one.";
    private const string _confirmUrl = "api/v1/auth/verify-email";
    private const string _resendUrl = "api/v1/users/me/email-verification";
    private const string _mineUrl = "api/v1/users/me";
    private const string _tenantUrl = "api/v1/tenants/me";

    [Fact]
    public async Task Signing_up_sends_one_confirmation_link_and_keeps_only_a_hash_of_its_secret()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);

        var mail = Assert.Single(await factory.ConfirmationsToAsync(tenant.Email));
        var token = EmailTestSupport.ConfirmationTokenIn(mail);
        var stored = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM email_verification_tokens WHERE token_hash = @hash AND length(token_hash) = 32",
            ("hash", SHA256.HashData(Encoding.ASCII.GetBytes(token))));
        var mine = await MineAsync(tenant.Client);

        Assert.Equal("Confirm your Cleared email address", mail.Subject);
        Assert.Contains($"http://localhost:4200/verify-email#{token}", mail.Body, StringComparison.Ordinal);
        Assert.Contains("within 24 hours", mail.Body, StringComparison.Ordinal);
        Assert.Equal(43, token.Length);
        Assert.Equal(1L, stored);
        Assert.Equal(tenant.Email, mine.Email);
        Assert.False(mine.EmailVerified);
    }

    [Fact]
    public async Task The_link_confirms_the_email_once_and_a_second_use_is_refused()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        var token = await TokenFromSignUpAsync(tenant);
        using var anonymous = factory.CreateAnonymousClient();

        using var confirmed = await ConfirmAsync(anonymous, token);
        var after = await MineAsync(tenant.Client);
        using var again = await ConfirmAsync(anonymous, token);
        var problem = await ProblemAsync(again);

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.True(after.EmailVerified);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(_linkProblem, Assert.Single(problem.Errors["token"]));
    }

    [Fact]
    public async Task A_scanner_that_only_fetches_the_link_cannot_confirm_the_email()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        var token = await TokenFromSignUpAsync(tenant);
        using var anonymous = factory.CreateAnonymousClient();

        using var fetched = await anonymous.GetAsync($"{_confirmUrl}?token={token}");
        using var head = await anonymous.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"{_confirmUrl}?token={token}"));
        var after = await MineAsync(tenant.Client);

        // No route answers a GET, so the default-deny rule turns it away before routing can say 405.
        Assert.Contains(fetched.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.MethodNotAllowed });
        Assert.Contains(head.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.MethodNotAllowed });
        Assert.False(after.EmailVerified);
        Assert.Equal(1L, await LiveLinksAsync(tenant.Email));
    }

    [Fact]
    public async Task A_link_works_for_a_day_and_not_a_minute_longer()
    {
        var clock = new MutableClock();
        using var host = WithClock(clock);
        using var anonymous = host.CreateAnonymousClient();
        var first = await factory.CreateTenantAsync(confirmEmail: false);
        var second = await factory.CreateTenantAsync(confirmEmail: false);
        var start = clock.UtcNow;

        var firstToken = await NewLinkAsync(host, first);
        var secondToken = await NewLinkAsync(host, second);
        clock.UtcNow = start.AddHours(23).AddMinutes(59);
        using var inTime = await ConfirmAsync(anonymous, firstToken);
        clock.UtcNow = start.AddHours(24).AddMinutes(1);
        using var late = await ConfirmAsync(anonymous, secondToken);

        Assert.Equal(HttpStatusCode.NoContent, inTime.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [Fact]
    public async Task Asking_again_ends_the_earlier_link_and_leaves_exactly_one_live()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        var first = await TokenFromSignUpAsync(tenant);
        using var anonymous = factory.CreateAnonymousClient();

        using var asked = await tenant.Client.PostAsync(_resendUrl, null);
        var second = EmailTestSupport.ConfirmationTokenIn((await factory.ConfirmationsToAsync(tenant.Email))[^1]);
        var live = await LiveLinksAsync(tenant.Email);
        using var withFirst = await ConfirmAsync(anonymous, first);
        using var withSecond = await ConfirmAsync(anonymous, second);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.NotEqual(first, second);
        Assert.Equal(1L, live);
        Assert.Equal(HttpStatusCode.BadRequest, withFirst.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withSecond.StatusCode);
    }

    [Fact]
    public async Task A_person_is_sent_three_links_an_hour_the_one_at_sign_up_included()
    {
        var clock = new MutableClock();
        using var host = WithClock(clock);
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        using var client = ClientFor(host, tenant);

        using var second = await client.PostAsync(_resendUrl, null);
        using var third = await client.PostAsync(_resendUrl, null);
        using var fourth = await client.PostAsync(_resendUrl, null);
        var sentSoFar = (await factory.ConfirmationsToAsync(tenant.Email, host)).Count;
        var wait = int.Parse(fourth.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
        var problem = await fourth.Content.ReadFromJsonAsync<ProblemDetails>();
        clock.UtcNow = clock.UtcNow.AddHours(1).AddSeconds(1);
        using var later = await client.PostAsync(_resendUrl, null);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, third.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal(3, sentSoFar);
        Assert.InRange(wait, 1, 3600);
        Assert.Equal("Too many confirmation emails. Try again later.", problem!.Title);
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
    }

    [Fact]
    public async Task An_account_whose_email_is_already_confirmed_is_sent_nothing()
    {
        var tenant = await factory.CreateTenantAsync();
        var before = (await factory.ConfirmationsToAsync(tenant.Email)).Count;

        using var response = await tenant.Client.PostAsync(_resendUrl, null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before, (await factory.ConfirmationsToAsync(tenant.Email)).Count);
    }

    [Fact]
    public async Task Only_a_signed_in_person_can_ask_for_a_link_or_see_the_account_status()
    {
        using var anonymous = factory.CreateAnonymousClient();

        using var status = await anonymous.GetAsync(_mineUrl);
        using var asked = await anonymous.PostAsync(_resendUrl, null);

        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asked.StatusCode);
    }

    [Fact]
    public async Task Ten_people_confirming_with_the_same_link_at_once_are_let_in_once()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        var token = await TokenFromSignUpAsync(tenant);
        using var anonymous = factory.CreateAnonymousClient();

        var responses = await ConcurrentCalls.FireAsync(10, () => ConfirmAsync(anonymous, token));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.True((await MineAsync(tenant.Client)).EmailVerified);
    }

    [Fact]
    public async Task Eight_requests_at_once_send_no_more_than_the_limit_and_leave_one_live_link()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);

        var responses = await ConcurrentCalls.FireAsync(8, () => tenant.Client.PostAsync(_resendUrl, null));
        var sent = await factory.ConfirmationsToAsync(tenant.Email);

        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(6, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(3, sent.Count);
        Assert.Equal(1L, await LiveLinksAsync(tenant.Email));
    }

    [Fact]
    public async Task A_link_confirms_only_the_account_it_was_sent_to()
    {
        var owner = await factory.CreateTenantAsync(confirmEmail: false);
        var other = await factory.CreateTenantAsync(confirmEmail: false);
        var token = await TokenFromSignUpAsync(owner);
        using var anonymous = factory.CreateAnonymousClient();

        using var confirmed = await ConfirmAsync(anonymous, token);

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.True((await MineAsync(owner.Client)).EmailVerified);
        Assert.False((await MineAsync(other.Client)).EmailVerified);
    }

    [Fact]
    public async Task A_link_of_the_wrong_shape_is_refused_like_any_other_bad_one()
    {
        using var anonymous = factory.CreateAnonymousClient();

        foreach (var bad in new[] { string.Empty, "short", new string('x', 100), new string('A', 43) })
        {
            using var response = await ConfirmAsync(anonymous, bad);
            var problem = await ProblemAsync(response);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(_linkProblem, Assert.Single(problem.Errors["token"]));
        }
    }

    [Fact]
    public async Task Banking_details_change_only_once_the_email_is_confirmed_and_other_changes_do_not_wait()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        var token = await TokenFromSignUpAsync(tenant);
        using var anonymous = factory.CreateAnonymousClient();

        using var addressOnly = await PutProfileAsync(tenant.Client, new("1 Main Road", null, null, null));
        using var refused = await PutProfileAsync(tenant.Client, new("1 Main Road", "FNB", "123", "250655"));
        var refusal = await refused.Content.ReadFromJsonAsync<JsonElement>();
        var untouched = await tenant.Client.GetFromJsonAsync<TenantResponse>(_tenantUrl, ApiJson.Options);
        using var confirmed = await ConfirmAsync(anonymous, token);
        using var allowed = await PutProfileAsync(tenant.Client, new("1 Main Road", "FNB", "123", "250655"));
        var saved = await tenant.Client.GetFromJsonAsync<TenantResponse>(_tenantUrl, ApiJson.Options);

        Assert.Equal(HttpStatusCode.OK, addressOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("email_not_verified", refusal.GetProperty("code").GetString());
        Assert.Null(untouched!.BankName);
        Assert.Equal("1 Main Road", untouched.Address);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("FNB", saved!.BankName);
    }

    [Fact]
    public async Task The_confirm_call_refuses_cross_site_requests_and_both_endpoints_answer_with_no_store()
    {
        var tenant = await factory.CreateTenantAsync(confirmEmail: false);
        using var anonymous = factory.CreateAnonymousClient();
        using var crossSite = new HttpRequestMessage(HttpMethod.Post, _confirmUrl)
        {
            Content = JsonContent.Create(new VerifyEmailRequest(new string('A', 43)), options: ApiJson.Options),
        };
        crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var sameSite = new HttpRequestMessage(HttpMethod.Post, _confirmUrl)
        {
            Content = JsonContent.Create(new VerifyEmailRequest(new string('A', 43)), options: ApiJson.Options),
        };
        sameSite.Headers.Add("Sec-Fetch-Site", "same-origin");

        using var refused = await anonymous.SendAsync(crossSite);
        using var answered = await anonymous.SendAsync(sameSite);
        using var status = await tenant.Client.GetAsync(_mineUrl);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, answered.StatusCode);
        Assert.True(answered.Headers.CacheControl?.NoStore);
        Assert.True(status.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("EmailVerification:TokenLifetime", "00:00:00")]
    [InlineData("EmailVerification:MaxRequestsPerWindow", "0")]
    [InlineData("EmailVerification:Window", "00:00:00")]
    public void Impossible_settings_stop_the_API_from_starting(string setting, string value)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("EmailVerification", StartupFailure.Messages(failure), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync(_confirmUrl, new VerifyEmailRequest(token), ApiJson.Options);

    private static Task<HttpResponseMessage> PutProfileAsync(HttpClient client, UpdateTenantProfileRequest request) =>
        client.PutAsJsonAsync(_tenantUrl, request, ApiJson.Options);

    private static async Task<UserResponse> MineAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>(_mineUrl, ApiJson.Options))!;

    private static async Task<ValidationProblemDetails> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;

    // The same token the shared copy issued works on a copy of the API with its own clock.
    private static HttpClient ClientFor(WebApplicationFactory<Program> host, TestTenant tenant)
    {
        var client = host.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = tenant.Client.DefaultRequestHeaders.Authorization;

        return client;
    }

    private WebApplicationFactory<Program> WithClock(MutableClock clock) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));

    private async Task<string> TokenFromSignUpAsync(TestTenant tenant) =>
        EmailTestSupport.ConfirmationTokenIn(Assert.Single(await factory.ConfirmationsToAsync(tenant.Email)));

    // Asks through a copy of the API with its own clock, so the new link is stamped with that clock.
    private async Task<string> NewLinkAsync(WebApplicationFactory<Program> host, TestTenant tenant)
    {
        using var client = ClientFor(host, tenant);
        using var response = await client.PostAsync(_resendUrl, null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return EmailTestSupport.ConfirmationTokenIn((await factory.ConfirmationsToAsync(tenant.Email, host))[^1]);
    }

    private Task<object?> LiveLinksAsync(string email) =>
        SessionTestSupport.ScalarAsync(
            factory,
            "SELECT count(*) FROM email_verification_tokens t JOIN \"AspNetUsers\" u ON u.id = t.user_id WHERE u.normalized_email = @email AND t.consumed_at IS NULL",
            ("email", email.ToUpperInvariant()));
}
