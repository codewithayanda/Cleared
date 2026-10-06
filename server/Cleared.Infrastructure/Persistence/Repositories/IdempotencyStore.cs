using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cleared.Infrastructure.Persistence.Repositories;

// Raw SQL because EF Core has no ON CONFLICT DO NOTHING. A claim on a key that another transaction
// holds uncommitted waits for that transaction to end, which is what makes concurrent duplicates
// safe. Raw SQL skips the tenant filter, so tenant_id is in every statement.
public sealed class IdempotencyStore(ClearedDbContext dbContext) : IIdempotencyStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    // Bounded and SKIP LOCKED, so cleaning up never waits on, or deadlocks with, another request.
    private const string PurgeSql = """
        DELETE FROM idempotency_keys
        WHERE (tenant_id, idempotency_key) IN (
            SELECT tenant_id, idempotency_key FROM idempotency_keys
            WHERE tenant_id = @tenantId AND created_at < @expiredBefore
            ORDER BY created_at
            LIMIT 100
            FOR UPDATE SKIP LOCKED);
        """;

    private const string ClaimSql = """
        INSERT INTO idempotency_keys (tenant_id, idempotency_key, fingerprint, created_at)
        VALUES (@tenantId, @key, @fingerprint, @createdAt)
        ON CONFLICT (tenant_id, idempotency_key) DO NOTHING;
        """;

    private const string ExistingSql = """
        SELECT fingerprint, response FROM idempotency_keys
        WHERE tenant_id = @tenantId AND idempotency_key = @key;
        """;

    private const string CompleteSql = """
        UPDATE idempotency_keys SET response = @response
        WHERE tenant_id = @tenantId AND idempotency_key = @key;
        """;

    public async Task<StoredResponse?> ClaimAsync(
        Guid tenantId, Guid key, string fingerprint, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var transaction = CurrentTransaction();
        var utcNow = now.ToUniversalTime();

        await ExecuteAsync(
            transaction, PurgeSql, cancellationToken, ("tenantId", tenantId), ("expiredBefore", utcNow - Retention));

        // A second pass covers an old row vanishing between the failed insert and the read.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var inserted = await ExecuteAsync(
                transaction, ClaimSql, cancellationToken,
                ("tenantId", tenantId), ("key", key), ("fingerprint", fingerprint), ("createdAt", utcNow));
            if (inserted == 1)
            {
                return null;
            }

            var earlier = await ReadAsync(transaction, tenantId, key, cancellationToken);
            if (earlier is not null)
            {
                return earlier;
            }
        }

        throw new InvalidOperationException("Could not claim the idempotency key.");
    }

    public async Task CompleteAsync(Guid tenantId, Guid key, string response, CancellationToken cancellationToken)
    {
        var updated = await ExecuteAsync(
            CurrentTransaction(), CompleteSql, cancellationToken,
            ("tenantId", tenantId), ("key", key), ("response", response));

        if (updated != 1)
        {
            throw new InvalidOperationException("The idempotency key was not claimed in this transaction.");
        }
    }

    private async Task<StoredResponse?> ReadAsync(
        DbTransaction transaction, Guid tenantId, Guid key, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, ExistingSql, ("tenantId", tenantId), ("key", key));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        // A committed key always has its answer, because the claim and the answer share a transaction.
        return new StoredResponse(reader.GetString(0), reader.GetString(1));
    }

    private async Task<int> ExecuteAsync(
        DbTransaction transaction, string sql, CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(transaction, sql, parameters);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [SuppressMessage(
        "Security", "CA2100", Justification = "Callers pass only the constant statements above; values are parameters.")]
    private DbCommand CreateCommand(DbTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    // A key claimed outside a transaction would commit on its own, and the guarantee would be gone.
    private DbTransaction CurrentTransaction() =>
        dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("An idempotency key needs an open transaction.");
}
