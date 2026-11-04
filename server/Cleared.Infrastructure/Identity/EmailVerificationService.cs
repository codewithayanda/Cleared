using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cleared.Infrastructure.Identity;

// A confirmation link is a random secret kept only as a hash. It works once, and it is the only live one
// a person has. Confirming sets the account's own flag, which is what everything else reads.
public sealed partial class EmailVerificationService(
    ClearedDbContext dbContext,
    IEmailQueue emailQueue,
    IClock clock,
    IOptions<EmailVerificationOptions> options,
    IOptions<AppOptions> app,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    private const int _purgeBatch = 50;

    // Finished links are kept this long past their end, then cleared a few at a time.
    private static readonly TimeSpan _finishedRetention = TimeSpan.FromDays(1);

    private readonly EmailVerificationOptions _options = options.Value;
    private readonly string _publicUrl = app.Value.PublicUrl.TrimEnd('/');

    private DbSet<EmailVerificationTokenRecord> Tokens => dbContext.Set<EmailVerificationTokenRecord>();

    private DbSet<ApplicationUser> Users => dbContext.Set<ApplicationUser>();

    public async Task<VerificationRequestOutcome> RequestAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The row lock makes one person's requests take turns, so each is counted and only one link is
        // ever live. The time is read once the lock is held, so a request that waited sees a later one.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE id = {userId} FOR UPDATE", cancellationToken);

        var user = await Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || user.EmailConfirmed)
        {
            return VerificationRequestOutcome.NothingToSend();
        }

        var now = clock.UtcNow;
        var sentLately = await Tokens
            .Where(t => t.UserId == userId && t.CreatedAt > now - _options.Window)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        if (sentLately.Count >= _options.MaxRequestsPerWindow)
        {
            LogRefused(logger, userId);

            // Another may go once enough of the earlier ones have left the window.
            return VerificationRequestOutcome.TooOften(
                sentLately[sentLately.Count - _options.MaxRequestsPerWindow] + _options.Window - now);
        }

        var secret = TokenSecret.Generate();

        await EndLiveLinksAsync(userId, now, cancellationToken);
        Tokens.Add(NewLink(userId, secret, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await PurgeFinishedLinksAsync(now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogIssued(logger, userId);
        Send(new EmailMessage(user.Email!, "Confirm your Cleared email address", Body($"{_publicUrl}/verify-email#{secret}")));

        return VerificationRequestOutcome.Sent();
    }

    public async Task<bool> ConfirmAsync(string token, CancellationToken cancellationToken)
    {
        if (!TokenSecret.TryHash(token, out var hash))
        {
            return false;
        }

        var now = clock.UtcNow;

        // The claim on the link and the flag on the account change together or not at all.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var presented = await Tokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (presented is null || !await TryClaimAsync(presented, now, cancellationToken))
        {
            return false;
        }

        await Users
            .Where(u => u.Id == presented.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, true), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogConfirmed(logger, presented.UserId);

        return true;
    }

    public async Task<bool> IsVerifiedAsync(Guid userId, CancellationToken cancellationToken) =>
        await Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.EmailConfirmed)
            .SingleOrDefaultAsync(cancellationToken);

    // The one step that must be atomic. Of requests carrying the same link, only one gets to change the
    // row, because the others wait for it and then find it used.
    private async Task<bool> TryClaimAsync(
        EmailVerificationTokenRecord presented, DateTimeOffset now, CancellationToken cancellationToken) =>
        await Tokens
            .Where(t => t.Id == presented.Id && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken) == 1;

    private Task<int> EndLiveLinksAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Tokens
            .Where(t => t.UserId == userId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), cancellationToken);

    private EmailVerificationTokenRecord NewLink(Guid userId, string secret, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = TokenSecret.TryHash(secret, out var hash) ? hash : throw new InvalidOperationException(),
        CreatedAt = now,
        ExpiresAt = now + _options.TokenLifetime,
    };

    // A link is kept until a day after it ends, which is long past the window that counts them.
    private Task<int> PurgeFinishedLinksAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _finishedRetention;

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM email_verification_tokens WHERE ctid IN (SELECT ctid FROM email_verification_tokens WHERE expires_at < {cutoff} LIMIT {_purgeBatch})",
            cancellationToken);
    }

    private void Send(EmailMessage message)
    {
        if (!emailQueue.TryEnqueue(message))
        {
            LogQueueFull(logger);
        }
    }

    private string Body(string link) =>
        $"""
        Welcome to Cleared. Confirm that this is your email address by opening this link within {SpanText.Describe(_options.TokenLifetime)}:

        {link}

        The link works once. If you did not create a Cleared account, ignore this email.
        """;

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Email confirmation link issued for user {UserId}.")]
    private static partial void LogIssued(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "A confirmation link was refused for user {UserId} because too many were sent lately.")]
    private static partial void LogRefused(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Email confirmed for user {UserId}.")]
    private static partial void LogConfirmed(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The email queue was full, so a message was dropped.")]
    private static partial void LogQueueFull(ILogger logger);
}
