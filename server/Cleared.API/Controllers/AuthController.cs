using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Controllers;

// UserManager/SignInManager are ASP.NET Core Identity's own framework services — used
// directly here rather than behind another Application-layer port, since the API project
// is already the composition root that wires up Infrastructure/Identity concerns (same
// pattern as Program.cs configuring the DbContext directly).
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterUserRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            TenantId = request.TenantId,
            Role = "Owner",
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(new { title = "Registration failed.", errors = result.Errors.Select(e => e.Description) });
        }

        var token = tokenService.IssueAccessToken(user.Id, user.TenantId, user.Role);

        return Created(string.Empty, new AuthResponse(token));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Unauthorized(new { title = "Invalid email or password." });
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized(new { title = "Invalid email or password." });
        }

        var token = tokenService.IssueAccessToken(user.Id, user.TenantId, user.Role);

        return Ok(new AuthResponse(token));
    }
}
