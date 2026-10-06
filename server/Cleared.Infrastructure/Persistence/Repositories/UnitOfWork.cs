using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class UnitOfWork(ClearedDbContext dbContext) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        // EF has no nested transactions. A caller already inside one joins it, and the owner of
        // that transaction decides whether it commits.
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return new JoinedTransaction();
        }

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        return new EfTransaction(transaction);
    }

    private sealed class EfTransaction(IDbContextTransaction transaction) : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class JoinedTransaction : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
