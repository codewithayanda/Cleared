using System.Data;
using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cleared.Infrastructure.Persistence.Repositories;

// Mirrors InvoiceNumberAllocator exactly, against its own credit_note_number_sequences
// table — a separate series with its own "CN-" prefix, per D-K. See InvoiceNumberAllocator
// for why this shape (UPSERT under Postgres's own row-level atomicity) is safe under
// concurrency; that reasoning applies here unchanged.
public sealed class CreditNoteNumberAllocator(ClearedDbContext dbContext) : ICreditNoteNumberAllocator
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
            INSERT INTO credit_note_number_sequences (tenant_id, year, next_value)
            VALUES (@tenantId, @year, 2)
            ON CONFLICT (tenant_id, year)
            DO UPDATE SET next_value = credit_note_number_sequences.next_value + 1
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

        return $"CN-{year}-{claimed:D4}";
    }
}
