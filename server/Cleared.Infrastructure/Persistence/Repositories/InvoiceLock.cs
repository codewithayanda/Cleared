using System.Data.Common;
using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cleared.Infrastructure.Persistence.Repositories;

// Raw SQL because EF Core has no FOR UPDATE. Same shape as InvoiceNumberAllocator: constant
// SQL, named parameters, enlisted in the current transaction. Raw SQL skips the global tenant
// filter, so tenant_id is in the WHERE clause. Proven by InvoiceConcurrencyTests.
public sealed class InvoiceLock(ClearedDbContext dbContext) : IInvoiceLock
{
    private const string LockSql = """
        SELECT 1 FROM invoices WHERE id = @invoiceId AND tenant_id = @tenantId FOR UPDATE;
        """;

    public async Task<bool> AcquireAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        // A lock taken outside a transaction is released at once, so refuse rather than pretend.
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Locking an invoice needs an open transaction.");

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = LockSql;
        AddParameter(command, "invoiceId", invoiceId);
        AddParameter(command, "tenantId", tenantId);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static void AddParameter(DbCommand command, string name, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
