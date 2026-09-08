namespace Cleared.Application.Abstractions;

public interface ITokenService
{
    string IssueAccessToken(Guid userId, Guid tenantId, string role);
}
