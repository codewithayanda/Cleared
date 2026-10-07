using System.Security.Cryptography;
using System.Text;
using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cleared.Infrastructure.Identity;

public sealed class SignInThrottle(
    ClearedDbContext dbContext, IClock clock, IOptions<SignInThrottleOptions> options) : ISignInThrottle
{
    private const int PurgeBatch = 50;

    private readonly SignInThrottleOptions _options = options.Value;

    private DbSet<SignInThrottleRecord> Throttles => dbContext.Set<SignInThrottleRecord>();

    public async Task<SignInTry> BeginAsync(string email, CancellationToken cancellationToken)
    {
        var hash = HashOf(email);
        var now = clock.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var created = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO sign_in_throttles (email_hash, attempts, updated_at) VALUES ({hash}, 0, {now}) ON CONFLICT (email_hash) DO NOTHING",
            cancellationToken);

        // The row lock makes callers for one email take turns, so every try is counted. Without it,
        // two guesses that arrive together would both read the same count.
        var row = await Throttles
            .FromSqlInterpolated($"SELECT * FROM sign_in_throttles WHERE email_hash = {hash} FOR UPDATE")
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var (attempts, lockedUntil, reserved) = Reserve(row, now);

        await Throttles
            .Where(t => t.EmailHash == hash)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.Attempts, attempts)
                    .SetProperty(t => t.LockedUntil, lockedUntil)
                    .SetProperty(t => t.UpdatedAt, now),
                cancellationToken);

        if (created == 1)
        {
            await PurgeAsync(now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return reserved;
    }

    public Task ClearAsync(string email, CancellationToken cancellationToken)
    {
        var hash = HashOf(email);

        return Throttles.Where(t => t.EmailHash == hash).ExecuteDeleteAsync(cancellationToken);
    }

    // The try that reaches MaxTries is still allowed, because the password may be right, but the lock
    // is set at once. A wrong answer to it reports the lock, and a right answer clears it.
    private (int Attempts, DateTimeOffset? LockedUntil, SignInTry Reserved) Reserve(
        SignInThrottleRecord row, DateTimeOffset now)
    {
        if (row.LockedUntil is { } until && until > now)
        {
            return (row.Attempts, until, new SignInTry(until - now, null));
        }

        var attempts = (row.LockedUntil is null ? row.Attempts : 0) + 1;

        return attempts < _options.MaxTries
            ? (attempts, null, new SignInTry(null, null))
            : (attempts, now + _options.LockDuration, new SignInTry(null, _options.LockDuration));
    }

    // Done only when a new row was added, so the cost follows new emails and stays small.
    private Task<int> PurgeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _options.ForgetAfter;

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM sign_in_throttles WHERE ctid IN (SELECT ctid FROM sign_in_throttles WHERE updated_at < {cutoff} LIMIT {PurgeBatch})",
            cancellationToken);
    }

    private static byte[] HashOf(string email) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant()));
}
