namespace Cleared.Infrastructure.Identity;

public sealed class SessionLifetimeOptions
{
    public const string SectionName = "Session";

    // A session ends after this long without a refresh. Each refresh starts the clock again.
    public TimeSpan IdleLifetime { get; set; } = TimeSpan.FromDays(7);

    // A session ends after this long however active it was, so a stolen session cannot be kept alive for ever.
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromDays(30);
}
