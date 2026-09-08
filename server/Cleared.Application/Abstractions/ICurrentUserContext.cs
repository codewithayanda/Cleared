namespace Cleared.Application.Abstractions;

// Resolved once per request from the validated JWT's sub claim — same reasoning as
// ITenantContext, but for "which user", not "which tenant". Kept as a separate interface
// rather than adding UserId to ITenantContext: they're two different questions, and a
// port should expose only the narrow thing a use case actually needs.
public interface ICurrentUserContext
{
    Guid UserId { get; }
}
