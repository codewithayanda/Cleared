namespace Cleared.Infrastructure.Identity;

public sealed class SignInThrottleOptions
{
    public const string SectionName = "SignInThrottle";

    // The try that reaches this number is the last one allowed before the lock.
    public int MaxTries { get; set; } = 5;

    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(15);

    // Counts nobody has added to for this long are deleted. It must outlast a lock.
    public TimeSpan ForgetAfter { get; set; } = TimeSpan.FromHours(24);
}
