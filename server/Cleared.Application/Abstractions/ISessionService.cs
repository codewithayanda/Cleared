namespace Cleared.Application.Abstractions;

// A signed-in browser session: a short-lived access token plus a refresh token that is swapped
// for a new one every time it is used. Why a refresh failed stays on the server, so a caller
// with a stolen or stale token learns nothing from the answer.
public interface ISessionService
{
    // For a user whose credentials were just checked.
    Task<SessionTokens> StartAsync(Guid userId, CancellationToken cancellationToken);

    Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    // Ends the session the token belongs to. A token nobody issued is ignored.
    Task EndAsync(string refreshToken, CancellationToken cancellationToken);

    // Ends every session of a user, for a password change or "sign out everywhere".
    Task EndAllAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record SessionTokens(string AccessToken, string RefreshToken, DateTimeOffset RefreshExpiresAt);

public enum RefreshFailure
{
    Malformed,
    Unknown,
    Revoked,
    Reused,
    Expired,
    UserMissing,
    SecurityStampChanged,
    LockedOut,
}

public sealed record RefreshOutcome(SessionTokens? Tokens, RefreshFailure? Failure)
{
    public static RefreshOutcome Succeeded(SessionTokens tokens) => new(tokens, null);

    public static RefreshOutcome Failed(RefreshFailure failure) => new(null, failure);
}
