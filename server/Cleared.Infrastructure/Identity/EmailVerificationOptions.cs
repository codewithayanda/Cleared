namespace Cleared.Infrastructure.Identity;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    // How long an emailed link works.
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(24);

    // A person may be sent this many links in one window, the one sent at sign-up included.
    public int MaxRequestsPerWindow { get; set; } = 3;

    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);
}
