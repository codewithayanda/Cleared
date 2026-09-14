namespace Cleared.Application.Abstractions;

// Resolved once per request from the validated JWT's sub claim, like ITenantContext.
// Separate from ITenantContext because "which user" and "which tenant" are different
// questions.
public interface ICurrentUserContext
{
    Guid UserId { get; }
}
