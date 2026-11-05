namespace Cleared.Infrastructure.Identity;

public sealed class SessionLifetimeOptions
{
    public const string SectionName = "Session";

    // A session ends after this long without a refresh. Each refresh starts the clock again. The web app
    // signs people out after 20 quiet minutes and renews an active session about every 10, so keep this
    // above 30 minutes.
    public TimeSpan IdleLifetime { get; set; } = TimeSpan.FromMinutes(30);

    // A session ends after this long however active it was, so a stolen session cannot be kept alive for ever.
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromDays(30);
}
