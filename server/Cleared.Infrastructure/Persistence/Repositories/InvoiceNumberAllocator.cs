using System.Data;
using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cleared.Infrastructure.Persistence.Repositories;

// Claims the next number for (tenant, year) in one round trip. ON CONFLICT DO UPDATE is
// atomic per row, so concurrent claims on the same (tenant_id, year) serialise in Postgres
// without an app-level lock. Proven by InvoiceNumberAllocatorConcurrencyTests.
// Call inside the invoice-save transaction, or a failure after the claim leaves a gap.
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
