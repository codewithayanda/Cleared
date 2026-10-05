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
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ITokenService tokenService,
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
