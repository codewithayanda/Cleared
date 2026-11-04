using Cleared.API.Security;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

// The signed-in person's own account. Nothing here takes an id: the person is the one the token names.
[ApiController]
[Authorize]
[NoStore]
[Route("api/v1/users/me")]
public sealed class UsersController(
    ICurrentUserContext currentUser,
    UserManager<ApplicationUser> userManager,
    IEmailVerificationService emailVerification) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserResponse>> GetMine()
    {
        var user = await userManager.FindByIdAsync(currentUser.UserId.ToString());

        return user is null ? NotFound() : Ok(new UserResponse(user.Email!, user.EmailConfirmed));
    }

    // 204 says there was nothing to send because the email is already confirmed.
    [HttpPost("email-verification")]
    public async Task<IActionResult> SendConfirmationLink(CancellationToken cancellationToken)
    {
        var outcome = await emailVerification.RequestAsync(currentUser.UserId, cancellationToken);

        return outcome.Result switch
        {
            VerificationRequestResult.Sent => Accepted(),
            VerificationRequestResult.NothingToSend => NoContent(),
            _ => TooManyRequestsResult.Create(this, outcome.RetryAfter!.Value, "Too many confirmation emails. Try again later."),
        };
    }
}
