namespace Cleared.Application.Auth;

public sealed record RegisterUserRequest(Guid TenantId, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(string AccessToken);
