namespace Cleared.API.RateLimiting;

// What one caller may do: a burst of PermitLimit requests, then PermitLimit more for every Window
// that passes, handed back steadily. The defaults suit production. They are settings so a test, or
// a busy office behind one address, can change them without a release.
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public RateLimitRule Login { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) };

    public RateLimitRule Register { get; set; } = new() { PermitLimit = 10, Window = TimeSpan.FromHours(1) };

    public RateLimitRule Session { get; set; } = new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) };

    public RateLimitRule Api { get; set; } = new() { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) };

    public IReadOnlyList<RateLimitRule> AllRules() => [Login, Register, Session, Api];
}

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}
