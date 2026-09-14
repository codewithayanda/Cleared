namespace Cleared.Application.Abstractions;

// Resolved once per request from the validated JWT's tenant_id claim, never from a request
// body or query string, so a client cannot choose which tenant it acts as.
public interface ITenantContext
{
    Guid TenantId { get; }
}
