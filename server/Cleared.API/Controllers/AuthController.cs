using Cleared.API.Security;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

// UserManager and SignInManager are used directly rather than behind an Application-layer
// port: the API project is already the composition root for Identity concerns, the same way
// Program.cs configures the DbContext directly.
[ApiController]
[AllowAnonymous]
[RejectCrossSite]
[NoStore]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ISessionService sessionService,
    RefreshCookie refreshCookie,
    PasswordTimingEqualizer passwordTiming,
    RegisterTenantService registerTenantService,
    IUnitOfWork unitOfWork) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request, CancellationToken cancellationToken)
    {
        // The company and its Owner are created together or not at all. A duplicate email or a
        // weak password must not leave an empty company behind.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var tenant = await registerTenantService.RegisterAsync(request.Company, cancellationToken);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = tenant.Id,
            Role = "Owner",
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { title = "Registration failed.", errors = result.Errors.Select(e => e.Description) });
        }

        await transaction.CommitAsync(cancellationToken);

        return Created(string.Empty, await StartSessionAsync(user, cancellationToken));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            passwordTiming.Spend(request.Password);

            return InvalidCredentials();
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        return Ok(await StartSessionAsync(user, cancellationToken));
    }

    // Trades the refresh cookie for a new access token and a new cookie. Every way of failing
    // looks the same from outside, and clears the cookie so the browser stops sending a dead one.
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        var presented = refreshCookie.Read(Request);
        if (presented is null)
        {
            return SessionEnded();
        }

        var outcome = await sessionService.RefreshAsync(presented, cancellationToken);
        if (outcome.Tokens is null)
        {
            return SessionEnded();
        }

        refreshCookie.Write(Response, outcome.Tokens.RefreshToken, outcome.Tokens.RefreshExpiresAt);

        return Ok(new AuthResponse(outcome.Tokens.AccessToken));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (refreshCookie.Read(Request) is { } presented)
        {
            await sessionService.EndAsync(presented, cancellationToken);
        }

        refreshCookie.Clear(Response);

        return NoContent();
    }

    // One browser holds one session, so signing in replaces whatever session it still had.
    private async Task<AuthResponse> StartSessionAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (refreshCookie.Read(Request) is { } previous)
        {
            await sessionService.EndAsync(previous, cancellationToken);
        }

        var session = await sessionService.StartAsync(user.Id, cancellationToken);
        refreshCookie.Write(Response, session.RefreshToken, session.RefreshExpiresAt);

        return new AuthResponse(session.AccessToken);
    }

    private ActionResult InvalidCredentials() => Unauthorized(new { title = "Invalid email or password." });

    private ActionResult SessionEnded()
    {
        refreshCookie.Clear(Response);

        return Unauthorized(new { title = "Your session has ended. Sign in again." });
    }
}
