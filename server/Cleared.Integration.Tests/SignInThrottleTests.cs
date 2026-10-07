using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cleared.Integration.Tests;

// The rate limit slows each address down. The throttle protects one email from being guessed, from
// however many addresses at once, so it is counted on the server, per email, and reserved before the
// password is checked.
[Collection(ApiCollection.Name)]
public class SignInThrottleTests(ClearedApiFactory factory)
{
    private const string Wrong = "Wrong-Password-1";

    [Fact]
    public async Task Five_wrong_passwords_lock_sign_in_and_the_fifth_is_told_so()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusOfAsync(client, session.Email, Wrong));
        }

        using var fifth = await SignInAsync(client, session.Email, Wrong);
        using var withTheRightPassword = await SignInAsync(client, session.Email, session.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        Assert.InRange(RetryAfter(fifth), 890, 900);
        Assert.Equal(HttpStatusCode.TooManyRequests, withTheRightPassword.StatusCode);
        Assert.InRange(RetryAfter(withTheRightPassword), 890, 900);
    }

    [Fact]
    public async Task The_right_password_on_the_fifth_try_still_signs_in_and_wipes_the_count()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await StatusOfAsync(client, session.Email, Wrong);
        }

        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, session.Email, session.Password));

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusOfAsync(client, session.Email, Wrong));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await StatusOfAsync(client, session.Email, Wrong));
    }

    [Fact]
    public async Task An_email_with_no_account_is_throttled_exactly_like_one_with_an_account()
    {
        using var client = factory.CreateAnonymousClient();
        var known = await SessionTestSupport.RegisterAsync(client);
        var unknown = $"nobody-{Guid.NewGuid():N}@example.test";

        var forKnown = await SixWrongPasswordsAsync(client, known.Email);
        var forUnknown = await SixWrongPasswordsAsync(client, unknown);

        Assert.Equal(forKnown, forUnknown);
        Assert.Equal(4, forKnown.Count(status => status == HttpStatusCode.Unauthorized));
        Assert.Equal(2, forKnown.Count(status => status == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Capital_letters_and_spaces_do_not_dodge_the_lock()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        foreach (var spelling in new[]
        {
            session.Email,
            session.Email.ToUpperInvariant(),
            $" {session.Email} ",
            session.Email,
            session.Email.ToUpperInvariant(),
        })
        {
            await StatusOfAsync(client, spelling, Wrong);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await StatusOfAsync(client, session.Email, session.Password));
    }

    [Fact]
    public async Task Locking_one_email_leaves_another_alone()
    {
        using var client = factory.CreateAnonymousClient();
        var locked = await SessionTestSupport.RegisterAsync(client);
        var other = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await StatusOfAsync(client, locked.Email, Wrong);
        }

        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, other.Email, other.Password));
    }

    [Fact]
    public async Task Another_address_sees_the_same_lock()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestAddressFilter>()));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await StatusOfAsync(client, session.Email, Wrong, from: "203.0.113.10");
        }

        var fromAnother = await StatusOfAsync(client, session.Email, session.Password, from: "203.0.113.20");

        Assert.Equal(HttpStatusCode.TooManyRequests, fromAnother);
    }

    [Fact]
    public async Task Fifty_wrong_passwords_at_once_check_the_password_five_times_and_no_more()
    {
        var hasher = new CountingPasswordHasher();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher);
        }));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        var responses = await ConcurrentCalls.FireAsync(
            50,
            () => client.PostAsJsonAsync(
                "api/v1/auth/login", new LoginRequest(session.Email, Wrong), ApiJson.Options));

        Assert.Equal(5, hasher.Verifications);
        Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(46, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task The_lock_ends_after_fifteen_minutes_and_counting_starts_again_from_nothing()
    {
        var clock = new MutableClock();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await StatusOfAsync(client, session.Email, Wrong);
        }

        clock.UtcNow = clock.UtcNow.AddMinutes(14);
        using var stillLocked = await SignInAsync(client, session.Email, session.Password);
        clock.UtcNow = clock.UtcNow.AddMinutes(1).AddSeconds(1);

        Assert.Equal(HttpStatusCode.TooManyRequests, stillLocked.StatusCode);
        Assert.InRange(RetryAfter(stillLocked), 55, 61);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusOfAsync(client, session.Email, Wrong));
        }

        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, session.Email, session.Password));
    }

    [Fact]
    public async Task Rows_nobody_has_touched_for_a_day_are_cleared_when_a_new_email_is_counted()
    {
        var old = SHA256.HashData(Encoding.UTF8.GetBytes($"OLD-{Guid.NewGuid():N}@EXAMPLE.TEST"));
        await SessionTestSupport.ExecuteAsync(
            factory,
            "INSERT INTO sign_in_throttles (email_hash, attempts, updated_at) VALUES (@hash, 3, @then)",
            ("hash", old),
            ("then", DateTimeOffset.UtcNow.AddDays(-2)));
        using var client = factory.CreateAnonymousClient();

        await StatusOfAsync(client, $"new-{Guid.NewGuid():N}@example.test", Wrong);

        var remaining = await SessionTestSupport.ScalarAsync(
            factory, "SELECT count(*) FROM sign_in_throttles WHERE email_hash = @hash", ("hash", old));
        Assert.Equal(0L, (long)remaining!);
    }

    [Theory]
    [InlineData("SignInThrottle:MaxTries", "0")]
    [InlineData("SignInThrottle:LockDuration", "00:00:00")]
    [InlineData("SignInThrottle:ForgetAfter", "00:05:00")]
    public void Impossible_settings_stop_the_API_from_starting(string setting, string value)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("SignInThrottle", StartupFailure.Messages(failure));
    }

    private static async Task<List<HttpStatusCode>> SixWrongPasswordsAsync(HttpClient client, string email)
    {
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            statuses.Add(await StatusOfAsync(client, email, Wrong));
        }

        return statuses;
    }

    private static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string email, string password, string? from = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password), options: ApiJson.Options),
        };

        if (from is not null)
        {
            request.Headers.Add(TestAddressFilter.Header, from);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpStatusCode> StatusOfAsync(
        HttpClient client, string email, string password, string? from = null)
    {
        using var response = await SignInAsync(client, email, password, from);

        return response.StatusCode;
    }

    private static int RetryAfter(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
}
