using Cleared.Application.Tenants;

namespace Cleared.Application.Auth;

// No tenant id on purpose: registering creates the company, so a caller can never choose
// which existing tenant they join.
public sealed record RegisterRequest(RegisterTenantRequest Company, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record ForgotPasswordRequest(string Email);

// The token is the secret in the emailed link. Nothing here names a user: the link does.
public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record AuthResponse(string AccessToken);
