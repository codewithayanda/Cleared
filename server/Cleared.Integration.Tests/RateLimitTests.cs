using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// Each test starts its own copy of the API with tiny limits. The shared copy is set so high that
// the rest of the suite can register hundreds of tenants from one address.
[Collection(ApiCollection.Name)]
public class RateLimitTests(ClearedApiFactory factory)
{
    private const string HalfAMinute = "00:00:30";
    private const string WrongPassword = "Wrong-Password-1";

    [Fact]
    public async Task Sign_in_over_its_limit_is_refused_with_a_retry_time_and_a_problem_body()
    {
        using var host = Limited(("Login", 3, HalfAMinute));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, session.Email, WrongPassword));
        }

        using var refused = await client.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest(session.Email, WrongPassword), ApiJson.Options);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.InRange(
            int.Parse(refused.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture), 1, 30);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem?.Status);
    }

    [Fact]
    public async Task A_refused_sign_in_never_reaches_the_handler_and_the_allowance_comes_back()
    {
        using var host = Limited(("Login", 2, "00:00:06"));
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        await SignInAsync(client, session.Email, WrongPassword);
        await SignInAsync(client, session.Email, WrongPassword);
        var refused = await SignInAsync(client, session.Email, session.Password);
        var failedAttempts = await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT access_failed_count FROM \"AspNetUsers\" WHERE normalized_email = @email",
            ("email", session.Email.ToUpperInvariant()));

        await Task.Delay(TimeSpan.FromSeconds(3.5));
        var afterTheWindow = await SignInAsync(client, session.Email, session.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused);
        Assert.Equal(2, failedAttempts);
        Assert.Equal(HttpStatusCode.OK, afterTheWindow);
    }

    [Fact]
    public async Task Each_address_gets_its_own_allowance()
    {
        using var host = Limited(("Login", 2, HalfAMinute));
        using var client = host.CreateAnonymousClient();

        await SignInAsync(client, "nobody@example.test", WrongPassword, from: "203.0.113.10");
        await SignInAsync(client, "nobody@example.test", WrongPassword, from: "203.0.113.10");
        var thirdFromTheSame = await SignInAsync(client, "nobody@example.test", WrongPassword, from: "203.0.113.10");
        var fromAnother = await SignInAsync(client, "nobody@example.test", WrongPassword, from: "203.0.113.20");

        Assert.Equal(HttpStatusCode.TooManyRequests, thirdFromTheSame);
        Assert.Equal(HttpStatusCode.Unauthorized, fromAnother);
    }

    [Fact]
    public async Task Registration_has_its_own_limit_apart_from_sign_in()
    {
        using var host = Limited(("Register", 2, HalfAMinute));
        using var client = host.CreateAnonymousClient();

        using var first = await SessionTestSupport.TryRegisterAsync(client);
        using var second = await SessionTestSupport.TryRegisterAsync(client);
        using var third = await SessionTestSupport.TryRegisterAsync(client);
        var signIn = await SignInAsync(client, "nobody@example.test", WrongPassword);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signIn);
    }

    [Fact]
    public async Task Refreshing_and_signing_out_share_one_allowance()
    {
        using var host = Limited(("Session", 3, HalfAMinute));
        using var client = host.CreateAnonymousClient();

        using var first = await client.SendAsync(SessionTestSupport.Refresh(null));
        using var second = await client.SendAsync(SessionTestSupport.Refresh(null));
        using var signOut = await client.SendAsync(SessionTestSupport.Logout(null));
        using var fourth = await client.SendAsync(SessionTestSupport.Refresh(null));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
    }

    [Fact]
    public async Task Signed_in_people_are_counted_one_by_one_not_all_together()
    {
        using var host = Limited(("Api", 4, HalfAMinute));
        var busy = await factory.CreateTenantAsync();
        var quiet = await factory.CreateTenantAsync();
        using var busyClient = ClientFor(host, busy);
        using var quietClient = ClientFor(host, quiet);

        for (var call = 0; call < 4; call++)
        {
            Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(busyClient, "api/v1/tenants/me"));
        }

        var refused = await StatusOfAsync(busyClient, "api/v1/tenants/me");
        var untouched = await StatusOfAsync(quietClient, "api/v1/tenants/me");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused);
        Assert.Equal(HttpStatusCode.OK, untouched);
    }

    [Fact]
    public async Task A_flood_of_calls_with_no_token_is_throttled_before_it_is_turned_away()
    {
        using var host = Limited(("Api", 2, HalfAMinute));
        using var client = host.CreateAnonymousClient();

        var first = await StatusOfAsync(client, "api/v1/customers");
        var second = await StatusOfAsync(client, "api/v1/customers");
        var third = await StatusOfAsync(client, "api/v1/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, first);
        Assert.Equal(HttpStatusCode.Unauthorized, second);
        Assert.Equal(HttpStatusCode.TooManyRequests, third);
    }

    [Fact]
    public async Task Health_checks_are_never_throttled()
    {
        using var host = Limited(("Api", 1, HalfAMinute));
        using var client = host.CreateAnonymousClient();

        for (var probe = 0; probe < 5; probe++)
        {
            Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, "health"));
        }
    }

    [Theory]
    [InlineData("RateLimits:Login:PermitLimit", "0")]
    [InlineData("RateLimits:Api:Window", "00:00:00")]
    public void Impossible_limits_stop_the_API_from_starting(string setting, string value)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("RateLimits", StartupFailure.Messages(failure));
    }

    private WebApplicationFactory<Program> Limited(params (string Rule, int Permits, string Window)[] limits) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (rule, permits, window) in limits)
            {
                builder.UseSetting($"RateLimits:{rule}:PermitLimit", permits.ToString(CultureInfo.InvariantCulture));
                builder.UseSetting($"RateLimits:{rule}:Window", window);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestAddressFilter>());
        });

    // The same token the shared copy issued works on the limited copy: both use one signing key.
    private static HttpClient ClientFor(WebApplicationFactory<Program> host, TestTenant tenant)
    {
        var client = host.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = tenant.Client.DefaultRequestHeaders.Authorization;

        return client;
    }

    private static async Task<HttpStatusCode> SignInAsync(
        HttpClient client, string email, string password, string? from = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password), options: ApiJson.Options),
        };

        if (from is not null)
        {
            request.Headers.Add(TestAddressFilter.Header, from);
        }

        using var response = await client.SendAsync(request);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);

        return response.StatusCode;
    }
}

// The test server has no remote address, so a test says which one a request came from.
public sealed class TestAddressFilter : IStartupFilter
{
    public const string Header = "X-Test-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, continueWith) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var address))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                }

                return continueWith(context);
            });

            next(app);
        };
}
