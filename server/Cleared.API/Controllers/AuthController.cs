using Cleared.API.RateLimiting;
using Cleared.API.Security;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Application.Tenants;
using Cleared.Domain.Common;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Cleared.API.Controllers;


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
    ISignInThrottle signInThrottle,
    IPasswordResetService passwordResetService,
    IEmailVerificationService emailVerificationService,
    RegisterTenantService registerTenantService,
    IUnitOfWork unitOfWork,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string _invalidEmailMessage = "Enter a valid email address.";
    private const string _invalidLinkMessage = "This link is invalid or has expired. Ask for a new one.";

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!EmailAddressRule.IsValid(request.Email))
        {
            return RegistrationFailed(new Dictionary<string, string[]> { ["email"] = [_invalidEmailMessage] });
        }

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
            return RegistrationFailed(DescribeFailures(result.Errors));
        }

        await transaction.CommitAsync(cancellationToken);
        await SendConfirmationLinkAsync(user.Id, cancellationToken);

        return Created(string.Empty, await StartSessionAsync(user, cancellationToken));
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        // The try is reserved before anything else, for an email with no account too, so the answer
        // is the same for both and guesses that arrive together cannot all be checked.
        var attempt = await signInThrottle.BeginAsync(request.Email, cancellationToken);
        if (attempt.Refused is { } wait)
        {
            return TooManyAttempts(wait);
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            passwordTiming.Spend(request.Password);

            return Failed(attempt);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return Failed(attempt);
        }

        await signInThrottle.ClearAsync(request.Email, cancellationToken);

        return Ok(await StartSessionAsync(user, cancellationToken));
    }

    // Trades the refresh cookie for a new access token and a new cookie. Every way of failing
    // looks the same from outside, and clears the cookie so the browser stops sending a dead one.
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Session)]
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
    [EnableRateLimiting(RateLimitPolicies.Session)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (refreshCookie.Read(Request) is { } presented)
        {
            await sessionService.EndAsync(presented, cancellationToken);
        }

        refreshCookie.Clear(Response);

        return NoContent();
    }

    // The same answer for an email with an account and one without, so it never shows which emails have one.
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.ForgotPassword)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!EmailAddressRule.IsValid(request.Email))
        {
            return FieldProblem("That is not an email address.", "email", _invalidEmailMessage);
        }

        var outcome = await passwordResetService.RequestAsync(request.Email, cancellationToken);

        return outcome.RetryAfter is { } wait
            ? TooManyAttempts(wait, "Too many reset requests for this email. Try again later.")
            : Accepted();
    }

    // Signs nobody in: the new password is typed again at the sign-in page.
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.ResetPassword)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var outcome = await passwordResetService.ResetAsync(request.Token, request.NewPassword, cancellationToken);

        return outcome.Result switch
        {
            ResetResult.Succeeded => NoContent(),
            ResetResult.InvalidLink => FieldProblem("Password reset failed.", "token", _invalidLinkMessage),
            _ => FieldProblem("Password reset failed.", "password", [.. outcome.PasswordProblems]),
        };
    }

    // The emailed link is the credential, so no sign-in is needed. Opening the link only shows a page. This
    // call, which the person makes with a button, is what confirms, so a mail scanner cannot use the link up.
    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimitPolicies.VerifyEmail)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        await emailVerificationService.ConfirmAsync(request.Token, cancellationToken)
            ? NoContent()
            : FieldProblem("Email confirmation failed.", "token", _invalidLinkMessage);

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

    // The account exists by now, so trouble sending this must not fail the sign-up. The page can resend.
    private async Task SendConfirmationLinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await emailVerificationService.RequestAsync(userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "The confirmation link for user {UserId} could not be sent ({Reason}).", userId, exception.GetType().Name);
        }
    }

    private ActionResult RegistrationFailed(Dictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(errors) { Title = "Registration failed." });

    private ActionResult FieldProblem(string title, string field, params string[] messages) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = messages })
        {
            Title = title,
        });

    // Field names match the form, so the page can put each message next to its box. Identity reports
    // a taken email twice (the second time as a username), so it gets one sentence of ours instead.
    private static Dictionary<string, string[]> DescribeFailures(IEnumerable<IdentityError> errors) =>
        errors
            .Select(error => error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" => new Failure("email", "An account with this email already exists."),
                "InvalidEmail" or "InvalidUserName" => new Failure("email", _invalidEmailMessage),
                var code when code.StartsWith("Password", StringComparison.Ordinal) => new Failure("password", error.Description),
                _ => new Failure(string.Empty, error.Description),
            })
            .Distinct()
            .GroupBy(failure => failure.Field, failure => failure.Message)
            .ToDictionary(group => group.Key, group => group.ToArray());

    // The last try allowed has already started the lock, so a wrong answer to it says so.
    private ActionResult Failed(SignInTry attempt) =>
        attempt.LockedIfWrong is { } wait ? TooManyAttempts(wait) : InvalidCredentials();

    private ActionResult TooManyAttempts(TimeSpan wait, string title = "Too many wrong passwords. Try again later.") =>
        TooManyRequestsResult.Create(this, wait, title);

    private ActionResult InvalidCredentials() => Unauthorized(new { title = "Invalid email or password." });

    private sealed record Failure(string Field, string Message);

    private ActionResult SessionEnded()
    {
        refreshCookie.Clear(Response);

        return Unauthorized(new { title = "Your session has ended. Sign in again." });
    }
}
