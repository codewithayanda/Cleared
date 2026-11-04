namespace Cleared.Infrastructure.Identity;

// A length of time in the words an email uses.
internal static class SpanText
{
    public static string Describe(TimeSpan span) =>
        span.TotalMinutes < 60
            ? Plural((int)Math.Ceiling(span.TotalMinutes), "minute")
            : Plural((int)Math.Ceiling(span.TotalHours), "hour");

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? string.Empty : "s")}";
}
