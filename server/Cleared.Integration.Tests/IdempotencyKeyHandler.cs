namespace Cleared.Integration.Tests;

// Behaves like the Angular client: every POST carries a fresh Idempotency-Key, unless the test
// sets its own or asks for none with WithoutKeyHeader.
public sealed class IdempotencyKeyHandler : DelegatingHandler
{
    public const string WithoutKeyHeader = "X-Test-Without-Idempotency-Key";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var withoutKey = request.Headers.Remove(WithoutKeyHeader);

        if (request.Method == HttpMethod.Post && !withoutKey && !request.Headers.Contains("Idempotency-Key"))
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
