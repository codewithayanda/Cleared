using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cleared.Infrastructure.Identity;

// Every use of a refresh token swaps it for a new one in the same family. A token that is used
// twice means a copy exists somewhere, so the whole family is revoked and the user signs in again.
// The reason a refresh failed is logged here and never sent to the caller.
public sealed partial class SessionService(
    ClearedDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    IClock clock,
    IOptions<SessionLifetimeOptions> options,
    ILogger<SessionService> logger) : ISessionService
{
    // Finished sessions are kept this long past their last day, so a late replay is still recognised.
    private static readonly TimeSpan FinishedRetention = TimeSpan.FromDays(1);

    private readonly SessionLifetimeOptions _options = options.Value;

    private DbSet<RefreshTokenRecord> Tokens => dbContext.Set<RefreshTokenRecord>();

    public async Task<SessionTokens> StartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Cannot start a session for a user that does not exist.");
        var now = clock.UtcNow;

        await Tokens
            .Where(t => t.UserId == userId && t.FamilyExpiresAt < now - FinishedRetention)
            .ExecuteDeleteAsync(cancellationToken);

        var familyId = Guid.NewGuid();
        var (record, secret) = NewToken(user, familyId, now, now + _options.AbsoluteLifetime);
        Tokens.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogStarted(logger, userId, familyId);

        return Issue(user, secret, record.ExpiresAt);
    }

    public async Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!RefreshTokenSecret.TryHash(refreshToken, out var hash))
        {
            return RefreshOutcome.Failed(RefreshFailure.Malformed);
        }

        var presented = await Tokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (presented is null)
        {
            return RefreshOutcome.Failed(RefreshFailure.Unknown);
        }

        var now = clock.UtcNow;

        if (presented.RevokedAt is not null)
        {
            LogRevokedPresented(logger, presented.UserId, presented.FamilyId);

            return RefreshOutcome.Failed(RefreshFailure.Revoked);
        }

        if (presented.UsedAt is not null)
        {
            return await RejectReuseAsync(presented, now, cancellationToken);
        }

        if (now >= presented.ExpiresAt || now >= presented.FamilyExpiresAt)
        {
            return RefreshOutcome.Failed(RefreshFailure.Expired);
        }

        var user = await userManager.FindByIdAsync(presented.UserId.ToString());
        if (user is null)
        {
            await RevokeFamilyAsync(presented.FamilyId, now, cancellationToken);
            LogUserMissing(logger, presented.UserId, presented.FamilyId);

            return RefreshOutcome.Failed(RefreshFailure.UserMissing);
        }

        if (user.SecurityStamp != presented.SecurityStamp)
        {
            await RevokeFamilyAsync(presented.FamilyId, now, cancellationToken);
            LogStampChanged(logger, user.Id, presented.FamilyId);

            return RefreshOutcome.Failed(RefreshFailure.SecurityStampChanged);
        }

        // A locked-out account may be under attack, so it gets no new access token. The session
        // is kept, not revoked, so the owner is not signed out by someone else's guessing.
        if (await userManager.IsLockedOutAsync(user))
        {
            LogLockedOut(logger, user.Id);

            return RefreshOutcome.Failed(RefreshFailure.LockedOut);
        }

        var (child, secret) = NewToken(user, presented.FamilyId, now, presented.FamilyExpiresAt);

        if (!await TryRotateAsync(presented, child, now, cancellationToken))
        {
            return await RejectLostRaceAsync(presented, now, cancellationToken);
        }

        return RefreshOutcome.Succeeded(Issue(user, secret, child.ExpiresAt));
    }

    public async Task EndAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!RefreshTokenSecret.TryHash(refreshToken, out var hash))
        {
            return;
        }

        var presented = await Tokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (presented is null)
        {
            return;
        }

        await RevokeFamilyAsync(presented.FamilyId, clock.UtcNow, cancellationToken);
        LogEnded(logger, presented.UserId, presented.FamilyId);
    }

    public async Task EndAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        await Tokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

        LogEndedAll(logger, userId);
    }

    // The one step that must be atomic. Of two requests carrying the same token, only one gets to
    // change the row, because the other waits for the first to commit and then finds it used.
    private async Task<bool> TryRotateAsync(
        RefreshTokenRecord presented, RefreshTokenRecord child, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var consumed = await Tokens
            .Where(t => t.Id == presented.Id && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.UsedAt, now).SetProperty(t => t.ReplacedById, child.Id),
                cancellationToken);

        if (consumed != 1)
        {
            return false;
        }

        Tokens.Add(child);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    // Lost the race for the token: either the session was ended a moment ago, or a second copy of
    // the token arrived at the same time as the first, which is exactly what a theft looks like.
    private async Task<RefreshOutcome> RejectLostRaceAsync(
        RefreshTokenRecord presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await Tokens.AsNoTracking().SingleAsync(t => t.Id == presented.Id, cancellationToken);

        if (current.RevokedAt is not null)
        {
            LogRevokedPresented(logger, current.UserId, current.FamilyId);

            return RefreshOutcome.Failed(RefreshFailure.Revoked);
        }

        return await RejectReuseAsync(current, now, cancellationToken);
    }

    private async Task<RefreshOutcome> RejectReuseAsync(
        RefreshTokenRecord presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await RevokeFamilyAsync(presented.FamilyId, now, cancellationToken);
        LogReuse(logger, presented.UserId, presented.FamilyId);

        return RefreshOutcome.Failed(RefreshFailure.Reused);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Tokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private (RefreshTokenRecord Record, string Secret) NewToken(
        ApplicationUser user, Guid familyId, DateTimeOffset now, DateTimeOffset familyExpiresAt)
    {
        var secret = RefreshTokenSecret.Generate();
        var idleExpiry = now + _options.IdleLifetime;

        var record = new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = RefreshTokenSecret.TryHash(secret, out var hash) ? hash : throw new InvalidOperationException(),
            SecurityStamp = user.SecurityStamp ?? string.Empty,
            CreatedAt = now,
            ExpiresAt = idleExpiry < familyExpiresAt ? idleExpiry : familyExpiresAt,
            FamilyExpiresAt = familyExpiresAt,
        };

        return (record, secret);
    }

    private SessionTokens Issue(ApplicationUser user, string secret, DateTimeOffset refreshExpiresAt) =>
        new(tokenService.IssueAccessToken(user.Id, user.TenantId, user.Role), secret, refreshExpiresAt);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Session started for user {UserId}, family {FamilyId}.")]
    private static partial void LogStarted(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Refresh token reuse detected for user {UserId}, family {FamilyId}. The whole family was revoked.")]
    private static partial void LogReuse(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "A revoked refresh token was presented for user {UserId}, family {FamilyId}.")]
    private static partial void LogRevokedPresented(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Security stamp changed for user {UserId}, so family {FamilyId} was revoked.")]
    private static partial void LogStampChanged(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Refresh refused for locked-out user {UserId}.")]
    private static partial void LogLockedOut(ILogger logger, Guid userId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Refresh refused because user {UserId} no longer exists, so family {FamilyId} was revoked.")]
    private static partial void LogUserMissing(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Session ended for user {UserId}, family {FamilyId}.")]
    private static partial void LogEnded(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Every session ended for user {UserId}.")]
    private static partial void LogEndedAll(ILogger logger, Guid userId);
}
