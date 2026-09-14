namespace Cleared.Application.Abstractions;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    // Wraps a number allocation and the invoice update it belongs to in one atomic unit.
    // A failure between claiming a number and saving the invoice leaves a permanent gap in
    // the sequence, which is a legal defect on a tax invoice.
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
