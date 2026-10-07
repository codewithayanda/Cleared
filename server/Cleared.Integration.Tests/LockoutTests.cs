using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Auth;

namespace Cleared.Integration.Tests;

// Rate limits slow a caller down. The lockout is what protects one account from being guessed.
[Collection(ApiCollection.Name)]
public class LockoutTests(ClearedApiFactory factory)
{
    private const string WrongPassword = "Wrong-Password-1";

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_fifteen_minutes_even_against_the_right_one()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInAsync(client, session.Email, WrongPassword));
        }

        var withTheRightPassword = await SignInAsync(client, session.Email, session.Password);
        var lockoutEnd = (DateTime)(await SessionTestSupport.ScalarAsync(
            factory,
            "SELECT lockout_end FROM \"AspNetUsers\" WHERE normalized_email = @email",
            ("email", session.Email.ToUpperInvariant())))!;

        Assert.Equal(HttpStatusCode.Unauthorized, withTheRightPassword);
        Assert.InRange(lockoutEnd - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Four_wrong_passwords_do_not_lock_the_account()
    {
        using var client = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await SignInAsync(client, session.Email, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.OK, await SignInAsync(client, session.Email, session.Password));
    }

    [Fact]
    public async Task Locking_one_account_leaves_another_alone()
    {
        using var client = factory.CreateAnonymousClient();
        var locked = await SessionTestSupport.RegisterAsync(client);
        var other = await SessionTestSupport.RegisterAsync(client);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await SignInAsync(client, locked.Email, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.OK, await SignInAsync(client, other.Email, other.Password));
    }

    private static async Task<HttpStatusCode> SignInAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest(email, password), ApiJson.Options);

        return response.StatusCode;
    }
}
