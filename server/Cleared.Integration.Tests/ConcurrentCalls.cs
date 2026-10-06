namespace Cleared.Integration.Tests;

public static class ConcurrentCalls
{
    // Releases every call at the same moment, so they really overlap. Each call must build its
    // own request, because a request message can only be sent once.
    public static async Task<IReadOnlyList<HttpResponseMessage>> FireAsync(
        int count, Func<Task<HttpResponseMessage>> send)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, count).Select(async _ =>
        {
            await gate.Task;

            return await send();
        }).ToList();

        gate.SetResult();

        return await Task.WhenAll(calls);
    }
}
