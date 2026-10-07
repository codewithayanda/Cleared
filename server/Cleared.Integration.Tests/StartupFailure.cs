namespace Cleared.Integration.Tests;

public static class StartupFailure
{
    // Everything the host said when it refused to start, wrapped exceptions included.
    public static string Messages(Exception? exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);

            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(" | ", messages);
    }
}
