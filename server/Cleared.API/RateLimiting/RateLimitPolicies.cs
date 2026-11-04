namespace Cleared.API.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Register = "register";
    public const string Session = "session";
    public const string ForgotPassword = "forgot-password";
    public const string ResetPassword = "reset-password";
    public const string VerifyEmail = "verify-email";
}
