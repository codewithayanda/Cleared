using Cleared.Application.Abstractions;

namespace Cleared.Infrastructure;

// South Africa has one timezone (SAST, UTC+2) and no daylight saving, so "today" is a fixed
// offset from UTC with no IANA timezone lookup needed.
public sealed class SystemClock : IClock
{
    private static readonly TimeSpan SastOffset = TimeSpan.FromHours(2);

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime((DateTimeOffset.UtcNow + SastOffset).DateTime);
}
