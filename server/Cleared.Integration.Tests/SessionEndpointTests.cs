using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Cleared.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// Ending a session has to really end it on the server, because the browser forgetting the token
// is not enough: a copy of the refresh cookie would keep working.
[Collection(ApiCollection.Name)]
public class SessionEndpointTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task Logging_out_ends_the_session_and_clears_the_cookie()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var logout = await client.SendAsync(SessionTestSupport.Logout(session.RefreshToken));
        using var afterwards = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("1970", SessionTestSupport.SetCookieLine(logout));
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public async Task Logging_out_with_no_or_an_unknown_cookie_still_answers_204(string? cookie)
    {
        using var client = factory.CreateAnonymousClient();

        using var logout = await client.SendAsync(SessionTestSupport.Logout(cookie));

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Logging_out_ends_only_that_browsers_session()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);
        using var otherBrowser = await SessionTestSupport.LoginAsync(client, session);
        var otherCookie = SessionTestSupport.RefreshCookieFrom(otherBrowser)!;

        using var logout = await client.SendAsync(SessionTestSupport.Logout(session.RefreshToken));
        using var stillSignedIn = await client.SendAsync(SessionTestSupport.Refresh(otherCookie));

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_replaces_the_session_the_browser_still_had()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        using var login = await SessionTestSupport.LoginAsync(client, session, session.RefreshToken);
        var replacement = SessionTestSupport.RefreshCookieFrom(login)!;
        using var oldOne = await client.SendAsync(SessionTestSupport.Refresh(session.RefreshToken));
        using var newOne = await client.SendAsync(SessionTestSupport.Refresh(replacement));

        Assert.Equal(HttpStatusCode.Unauthorized, oldOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newOne.StatusCode);
    }

    [Fact]
    public async Task Ending_every_session_of_a_user_leaves_other_users_alone()
    {
        using var client = factory.CreateAnonymousClient();
        var leaving = await SessionTestSupport.RegisterAsync(client);
        using var secondBrowser = await SessionTestSupport.LoginAsync(client, leaving);
        var secondCookie = SessionTestSupport.RefreshCookieFrom(secondBrowser)!;
        var staying = await SessionTestSupport.RegisterAsync(client);
        var userId = (Guid)(await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT id FROM \"AspNetUsers\" WHERE normalized_email = @email",
            ("email", leaving.Email.ToUpperInvariant())))!;

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISessionService>()
                .EndAllAsync(userId, CancellationToken.None);
        }

        using var first = await client.SendAsync(SessionTestSupport.Refresh(leaving.RefreshToken));
        using var second = await client.SendAsync(SessionTestSupport.Refresh(secondCookie));
        using var other = await client.SendAsync(SessionTestSupport.Refresh(staying.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task Every_auth_endpoint_refuses_a_cross_site_request(string endpoint)
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/auth/{endpoint}")
        {
            Content = JsonContent.Create(
                new RegisterRequest(
                    new RegisterTenantRequest("Cross Site Co", VatStatus.NotRegistered, null, null),
                    $"cross-{Guid.NewGuid():N}@example.test",
                    SessionTestSupport.StrongPassword),
                options: ApiJson.Options),
        };
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
