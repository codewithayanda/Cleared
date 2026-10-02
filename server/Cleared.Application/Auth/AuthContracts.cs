using Cleared.Application.Tenants;

namespace Cleared.Application.Auth;

// No tenant id on purpose: registering creates the company, so a caller can never choose
// which existing tenant they join.
public sealed record RegisterRequest(RegisterTenantRequest Company, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(string AccessToken);
