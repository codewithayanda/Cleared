namespace Cleared.Application.Abstractions;

// DateTime.UtcNow / DateTimeOffset.Now are a hidden dependency on the real world that make
// date-sensitive logic (IsOverdue, which VAT period a date falls in) untestable without an
// indirection like this one. Cleared operates in SAST (UTC+2, no DST); Today() returns the
// SAST calendar date, since tax dates are calendar facts, not instants.
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}
