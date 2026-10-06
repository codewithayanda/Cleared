using Cleared.Application.Abstractions;
using Cleared.Application.Idempotency;
using Cleared.Application.Tests.TestDoubles;

namespace Cleared.Application.Tests.Idempotency;

// The flow only. Rollback, waiting on a held key and the unique index are Postgres behaviour,
// proven against a real database in IdempotencyTests.
public class IdempotentExecutorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly FakeIdempotencyStore _store = new();
    private readonly RecordingUnitOfWork _unitOfWork = new();

    private IdempotentExecutor CreateExecutor() =>
        new(_store, _unitOfWork, new FakeClock(new DateOnly(2026, 10, 6)));

    [Fact]
    public async Task A_new_key_runs_the_work_stores_the_answer_and_commits()
    {
        var runs = 0;

        var result = await CreateExecutor().ExecuteAsync(
            Tenant, Guid.NewGuid(), "Op", new { A = 1 },
            () =>
            {
                runs++;
                return Task.FromResult(new Reply(Guid.NewGuid(), "first"));
            },
            CancellationToken.None);

        Assert.False(result.Replayed);
        Assert.Equal("first", result.Value.Text);
        Assert.Equal(1, runs);
        Assert.Equal(1, _store.Completed);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task A_repeat_returns_the_stored_answer_without_running_the_work_again()
    {
        var executor = CreateExecutor();
        var key = Guid.NewGuid();
        var runs = 0;
        Task<Reply> Work()
        {
            runs++;
            return Task.FromResult(new Reply(Guid.NewGuid(), "only"));
        }

        var first = await executor.ExecuteAsync(Tenant, key, "Op", new { A = 1 }, Work, CancellationToken.None);
        var repeat = await executor.ExecuteAsync(Tenant, key, "Op", new { A = 1 }, Work, CancellationToken.None);

        Assert.True(repeat.Replayed);
        Assert.Equal(first.Value, repeat.Value);
        Assert.Equal(1, runs);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task The_same_key_for_a_different_request_is_refused_and_runs_nothing()
    {
        var executor = CreateExecutor();
        var key = Guid.NewGuid();
        var runs = 0;
        Task<Reply> Work()
        {
            runs++;
            return Task.FromResult(new Reply(Guid.NewGuid(), "x"));
        }

        await executor.ExecuteAsync(Tenant, key, "Op", new { A = 1 }, Work, CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyReusedException>(
            () => executor.ExecuteAsync(Tenant, key, "Op", new { A = 2 }, Work, CancellationToken.None));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task The_same_key_for_a_different_operation_is_refused()
    {
        var executor = CreateExecutor();
        var key = Guid.NewGuid();
        Task<Reply> Work() => Task.FromResult(new Reply(Guid.NewGuid(), "x"));

        await executor.ExecuteAsync(Tenant, key, "RecordPayment", new { A = 1 }, Work, CancellationToken.None);

        await Assert.ThrowsAsync<IdempotencyKeyReusedException>(
            () => executor.ExecuteAsync(Tenant, key, "CreateCreditNote", new { A = 1 }, Work, CancellationToken.None));
    }

    [Fact]
    public async Task A_null_result_is_not_remembered_and_not_committed()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Tenant, Guid.NewGuid(), "Op", new { A = 1 }, () => Task.FromResult<Reply?>(null), CancellationToken.None);

        Assert.Null(result.Value);
        Assert.False(result.Replayed);
        Assert.Equal(0, _store.Completed);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    [Fact]
    public async Task A_failing_action_stores_nothing_and_commits_nothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExecutor().ExecuteAsync<Reply>(
            Tenant, Guid.NewGuid(), "Op", new { A = 1 }, () => throw new InvalidOperationException("no"),
            CancellationToken.None));

        Assert.Equal(0, _store.Completed);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    private sealed record Reply(Guid Id, string Text);

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<(Guid Tenant, Guid Key), (string Fingerprint, string Response)> _rows = [];

        public int Completed { get; private set; }

        public Task<StoredResponse?> ClaimAsync(
            Guid tenantId, Guid key, string fingerprint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (_rows.TryGetValue((tenantId, key), out var row))
            {
                return Task.FromResult<StoredResponse?>(new StoredResponse(row.Fingerprint, row.Response));
            }

            _rows[(tenantId, key)] = (fingerprint, string.Empty);

            return Task.FromResult<StoredResponse?>(null);
        }

        public Task CompleteAsync(Guid tenantId, Guid key, string response, CancellationToken cancellationToken)
        {
            _rows[(tenantId, key)] = (_rows[(tenantId, key)].Fingerprint, response);
            Completed++;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ITransaction>(new RecordingTransaction(this));

        private sealed class RecordingTransaction(RecordingUnitOfWork owner) : ITransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                owner.Commits++;

                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
