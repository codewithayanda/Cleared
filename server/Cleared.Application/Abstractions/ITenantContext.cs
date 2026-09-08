namespace Cleared.Application.Abstractions;

// Resolved once per request from the validated JWT's tenant_id claim — never from a
// request body or query string. That is the entire point: a client cannot choose which
// tenant it acts as by changing a parameter.
public interface ITenantContext
{
    Guid TenantId { get; }
}
