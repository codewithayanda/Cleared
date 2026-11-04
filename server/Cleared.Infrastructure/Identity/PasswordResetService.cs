using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cleared.Infrastructure.Identity;

// A reset link is a random secret kept only as a hash. It works once, and it is the only live one a
// person has. Asking for a link answers the same whether or not the email has an account.
public sealed partial class PasswordResetService(
    ClearedDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ISessionService sessionService,
    ISignInThrottle signInThrottle,
    IEmailQueue emailQueue,
    IClock clock,
    IOptions<PasswordResetOptions> options,
    IOptions<AppOptions> app,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private const int _purgeBatch = 50;

    // Finished links and counts are kept this long past their end, then cleared a few at a time.
    private static readonly TimeSpan _finishedRetention = TimeSpan.FromDays(1);

    private readonly PasswordResetOptions _options = options.Value;
    private readonly string _publicUrl = app.Value.PublicUrl.TrimEnd('/');

    private DbSet<PasswordResetTokenRecord> Tokens => dbContext.Set<PasswordResetTokenRecord>();

    private DbSet<PasswordResetThrottleRecord> Throttles => dbContext.Set<PasswordResetThrottleRecord>();

    public async Task<ResetRequestOutcome> RequestAsync(string email, CancellationToken cancellationToken)
    {
        // The count, the old links and the new link change together or not at all, and the count's row
        // lock makes requests for one email take turns from start to finish.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var (wait, now) = await ReserveAsync(email, cancellationToken);
        if (wait is { } refused)
        {
            LogRefused(logger);

            return new ResetRequestOutcome(refused);
        }

        var user = await userManager.FindByEmailAsync(email);
        var secret = TokenSecret.Generate();

        // An email with no account takes the same steps, with nothing to find and nothing to save.
        await EndLiveLinksAsync(user?.Id ?? Guid.NewGuid(), now, cancellationToken);

        if (user is not null)
        {
            Tokens.Add(NewLink(user.Id, secret, now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await PurgeFinishedLinksAsync(now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (user is not null)
        {
            LogIssued(logger, user.Id);
            Send(new EmailMessage(user.Email!, "Reset your Cleared password", ResetBody($"{_publicUrl}/reset-password#{secret}")));
        }

        return new ResetRequestOutcome(null);
    }

    public async Task<ResetOutcome> ResetAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        if (!TokenSecret.TryHash(token, out var hash))
        {
            return ResetOutcome.InvalidLink();
        }

        var now = clock.UtcNow;

        // Leaving without a commit undoes everything below, the claim on the link included, so a
        // password that breaks the rules leaves the link ready for another try.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var presented = await Tokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (presented is null || !await TryClaimAsync(presented, now, cancellationToken))
        {
            return ResetOutcome.InvalidLink();
        }

        var user = await userManager.FindByIdAsync(presented.UserId.ToString());
        if (user is null)
        {
            return ResetOutcome.InvalidLink();
        }

        // Identity does the changing, so the rules, the hashing and the new security stamp are its own.
        // The token it asks for is made and used in this one call, so it never leaves the server.
        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, newPassword);
        if (!result.Succeeded)
        {
            return Refused(result);
        }

        await EndLiveLinksAsync(user.Id, now, cancellationToken);
        await sessionService.EndAllAsync(user.Id, cancellationToken);
        await signInThrottle.ClearAsync(user.Email!, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogReset(logger, user.Id);
        Send(new EmailMessage(user.Email!, "Your Cleared password was changed", ChangedBody()));

        return ResetOutcome.Succeeded();
    }

    // The one step that must be atomic. Of requests carrying the same link, only one gets to change the
    // row, because the others wait for it and then find it used, or find it free again if it gave up.
    private async Task<bool> TryClaimAsync(
        PasswordResetTokenRecord presented, DateTimeOffset now, CancellationToken cancellationToken) =>
        await Tokens
            .Where(t => t.Id == presented.Id && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken) == 1;

    // Reserves one request for the email, and says how long to wait when it has used them all. Also
    // returns the time, which is read only once the row is locked: a caller that waited for the lock
    // then sees a time after the one that went before it, so no wait is ever longer than the window.
    private async Task<(TimeSpan? Wait, DateTimeOffset Now)> ReserveAsync(string email, CancellationToken cancellationToken)
    {
        var hash = EmailKey.Hash(email);
        var firstSeen = clock.UtcNow;

        var created = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO password_reset_throttles (email_hash, window_start, requests) VALUES ({hash}, {firstSeen}, 0) ON CONFLICT (email_hash) DO NOTHING",
            cancellationToken);

        // The row lock makes callers for one email take turns, so every request is counted.
        var row = await Throttles
            .FromSqlInterpolated($"SELECT * FROM password_reset_throttles WHERE email_hash = {hash} FOR UPDATE")
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var now = clock.UtcNow;
        var windowEnded = now - row.WindowStart >= _options.Window;
        var windowStart = windowEnded ? now : row.WindowStart;
        var requests = windowEnded ? 0 : row.Requests;

        if (requests >= _options.MaxRequestsPerWindow)
        {
            return (windowStart + _options.Window - now, now);
        }

        await Throttles
            .Where(t => t.EmailHash == hash)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.WindowStart, windowStart).SetProperty(t => t.Requests, requests + 1),
                cancellationToken);

        if (created == 1)
        {
            await PurgeCountsAsync(now, cancellationToken);
        }

        return (null, now);
    }

    private Task<int> EndLiveLinksAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Tokens
            .Where(t => t.UserId == userId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

    private PasswordResetTokenRecord NewLink(Guid userId, string secret, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = TokenSecret.TryHash(secret, out var hash) ? hash : throw new InvalidOperationException(),
        CreatedAt = now,
        ExpiresAt = now + _options.TokenLifetime,
    };

    // Done on every request that gets this far, so the cost does not depend on whether the email is real.
    private Task<int> PurgeFinishedLinksAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _finishedRetention;

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM password_reset_tokens WHERE ctid IN (SELECT ctid FROM password_reset_tokens WHERE expires_at < {cutoff} LIMIT {_purgeBatch})",
            cancellationToken);
    }

    // Done only when a new row was added, so the cost follows new emails and stays small.
    private Task<int> PurgeCountsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _options.Window - _finishedRetention;

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM password_reset_throttles WHERE ctid IN (SELECT ctid FROM password_reset_throttles WHERE window_start < {cutoff} LIMIT {_purgeBatch})",
            cancellationToken);
    }

    // A reason other than the password's own rules is our bug and not the caller's, so it is a 500.
    private static ResetOutcome Refused(IdentityResult result)
    {
        var problems = result.Errors
            .Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
            .Select(error => error.Description)
            .ToList();

        return problems.Count > 0
            ? ResetOutcome.WeakPassword(problems)
            : throw new InvalidOperationException(
                $"Resetting a password failed for a reason other than its rules: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
    }

    private void Send(EmailMessage message)
    {
        if (!emailQueue.TryEnqueue(message))
        {
            LogQueueFull(logger);
        }
    }

    private string ResetBody(string link) =>
        $"""
        Someone asked to reset the password for your Cleared account. If that was you, open this link within {SpanText.Describe(_options.TokenLifetime)}:

        {link}

        The link works once. If you did not ask for this, ignore this email and your password stays as it is.
        """;

    private string ChangedBody() =>
        $"""
        The password for your Cleared account was just changed, and every device was signed out.

        If that was you, there is nothing more to do. If it was not, reset your password again now: {_publicUrl}/forgot-password
        """;

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Password reset link issued for user {UserId}.")]
    private static partial void LogIssued(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "A password reset request was refused because its email asked too often.")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Password reset for user {UserId}. Every session of theirs ended.")]
    private static partial void LogReset(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The email queue was full, so a message was dropped.")]
    private static partial void LogQueueFull(ILogger logger);
}
