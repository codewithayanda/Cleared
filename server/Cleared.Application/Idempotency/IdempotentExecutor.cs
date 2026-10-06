using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cleared.Application.Abstractions;

namespace Cleared.Application.Idempotency;

public sealed record IdempotentResult<T>(T Value, bool Replayed);

// Runs an action at most once per (tenant, key). The claim, the work and the stored answer share
// one transaction, so a failure or a crash leaves nothing behind and a retry runs fresh.
public sealed class IdempotentExecutor(IIdempotencyStore store, IUnitOfWork unitOfWork, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // operation and request say what the key is for. Using the key for anything else is refused,
    // because the stored answer would be wrong for it. A null result means nothing happened, so it
    // is not remembered.
    public async Task<IdempotentResult<T>> ExecuteAsync<T>(
        Guid tenantId,
        Guid key,
        string operation,
        object request,
        Func<Task<T>> work,
        CancellationToken cancellationToken)
        where T : class?
    {
        var fingerprint = Fingerprint(operation, request);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var earlier = await store.ClaimAsync(tenantId, key, fingerprint, clock.UtcNow, cancellationToken);
        if (earlier is not null)
        {
            if (earlier.Fingerprint != fingerprint)
            {
                throw new IdempotencyKeyReusedException();
            }

            // Only non-null results are stored, so this is never null.
            return new IdempotentResult<T>(JsonSerializer.Deserialize<T>(earlier.Response, Json)!, Replayed: true);
        }

        var result = await work();

        // Leaving without a commit rolls the claim back.
        if (result is null)
        {
            return new IdempotentResult<T>(result, Replayed: false);
        }

        await store.CompleteAsync(tenantId, key, JsonSerializer.Serialize(result, Json), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IdempotentResult<T>(result, Replayed: false);
    }

    private static string Fingerprint(string operation, object request)
    {
        var text = $"{operation}\n{JsonSerializer.Serialize(request, Json)}";

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
