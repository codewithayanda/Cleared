using Cleared.Application.Idempotency;

namespace Cleared.API.Idempotency;

public static class IdempotencyHeader
{
    public const string Name = "Idempotency-Key";

    public const string ReplayedName = "Idempotent-Replayed";

    // Lets a client, or a test, see that the answer came from the first attempt.
    public static void MarkReplayed<T>(this HttpResponse response, IdempotentResult<T> result)
    {
        if (result.Replayed)
        {
            response.Headers[ReplayedName] = "true";
        }
    }
}
