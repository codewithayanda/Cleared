namespace Cleared.Application.Abstractions;

// Remembers which (tenant, key) pairs have been served and what the answer was. Both calls need
// an open transaction, so a claim commits or rolls back together with the work it guards.
public interface IIdempotencyStore
{
    // Returns null when this call now owns the key, or the earlier call's fingerprint and answer.
    // A key another transaction holds uncommitted makes this wait for that transaction to end.
    Task<StoredResponse?> ClaimAsync(
        Guid tenantId, Guid key, string fingerprint, DateTimeOffset now, CancellationToken cancellationToken);

    Task CompleteAsync(Guid tenantId, Guid key, string response, CancellationToken cancellationToken);
}

public sealed record StoredResponse(string Fingerprint, string Response);
