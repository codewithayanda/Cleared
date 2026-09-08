using System.Data;
using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cleared.Infrastructure.Persistence.Repositories;

// Claims the next number for (tenant, year) in one round trip using Postgres's UPSERT.
// INSERT ... ON CONFLICT DO UPDATE is atomic at the row level — Postgres itself serialises
// concurrent callers for the same (tenant_id, year) row, so this is safe under concurrency
// without an explicit application-level row lock. It should run inside the same
// transaction as the invoice save (see InvoiceService.IssueAsync) so a later failure
// rolls back the claim too — otherwise a save failure after this call leaves a permanent
// gap — but it doesn't depend on one: it opens the connection itself if nothing already
// has, rather than silently assuming a caller opened it first.
public sealed class InvoiceNumberAllocator(ClearedDbContext dbContext) : IInvoiceNumberAllocator
{
    public async Task<string> AllocateAsync(Guid tenantId, int year, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO invoice_number_sequences (tenant_id, year, next_value)
            VALUES (@tenantId, @year, 2)
            ON CONFLICT (tenant_id, year)
            DO UPDATE SET next_value = invoice_number_sequences.next_value + 1
            RETURNING next_value - 1;
            """;

        var tenantParam = command.CreateParameter();
        tenantParam.ParameterName = "tenantId";
        tenantParam.Value = tenantId;
        command.Parameters.Add(tenantParam);

        var yearParam = command.CreateParameter();
        yearParam.ParameterName = "year";
        yearParam.Value = year;
        command.Parameters.Add(yearParam);

        var claimed = (int)(await command.ExecuteScalarAsync(cancellationToken))!;

        return $"INV-{year}-{claimed:D4}";
    }
}
