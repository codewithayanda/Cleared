namespace Cleared.Infrastructure.Identity;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    // How long an emailed link works.
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    // An email may ask for this many links in one window, whether or not it has an account.
    public int MaxRequestsPerWindow { get; set; } = 3;

    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);
}
