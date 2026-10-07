using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// Fails when an endpoint is added without a tenant isolation case, so a new route cannot
// reach production unexamined. Endpoints that skip the suite are listed here with the reason.
[Collection(ApiCollection.Name)]
public class EndpointGuardTests(ClearedApiFactory factory)
{
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        ["POST api/v1/auth/register"] = "anonymous by design, it creates its own tenant (RegistrationTests)",
        ["POST api/v1/auth/login"] = "anonymous by design, it checks credentials and issues the token",
        ["POST api/v1/auth/refresh"] = "anonymous by design, the refresh cookie is the credential (RefreshTokenTests)",
        ["POST api/v1/auth/logout"] = "anonymous by design, it ends the session the cookie names (SessionEndpointTests)",
        ["* health"] = "returns no data",
        ["* health/ready"] = "returns no data",
    };

    [Fact]
    public void Every_endpoint_has_an_isolation_case_or_a_recorded_reason_to_be_exempt()
    {
        var covered = IsolationCases.All.Select(c => c.Endpoint).ToHashSet();

        var uncovered = Routes()
            .Select(route => route.Key)
            .Where(key => !covered.Contains(key) && !Exempt.ContainsKey(key))
            .ToList();

        Assert.True(
            uncovered.Count == 0,
            $"No isolation case or exemption for: {string.Join(", ", uncovered)}. " +
            "Add an IsolationCase to IsolationCases.All, or an entry to Exempt with the reason.");
    }

    [Fact]
    public void Every_endpoint_that_is_not_exempt_requires_authorization()
    {
        var open = Routes()
            .Where(route => !Exempt.ContainsKey(route.Key))
            .Where(route => route.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || route.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(route => route.Key)
            .ToList();

        Assert.True(open.Count == 0, $"Reachable without a token: {string.Join(", ", open)}. Add [Authorize].");
    }

    [Fact]
    public void The_cases_and_the_exemptions_name_only_endpoints_that_exist()
    {
        var existing = Routes().Select(route => route.Key).ToHashSet();

        var stale = IsolationCases.All.Select(c => c.Endpoint)
            .Concat(Exempt.Keys)
            .Where(key => !existing.Contains(key))
            .ToList();

        Assert.True(stale.Count == 0, $"No such endpoint: {string.Join(", ", stale)}. Remove or rename it.");
    }

    [Fact]
    public void No_endpoint_is_listed_twice()
    {
        var duplicated = IsolationCases.All.Select(c => c.Endpoint)
            .Concat(Exempt.Keys)
            .GroupBy(key => key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicated.Count == 0, $"Listed more than once: {string.Join(", ", duplicated)}.");
    }

    // Read from the routing table of the running API, so nothing can be added without showing up.
    // "*" stands for an endpoint that answers every method.
    private List<Route> Routes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"];
                var pattern = endpoint.RoutePattern.RawText!.TrimStart('/');

                return methods.Select(method => new Route($"{method} {pattern}", endpoint));
            })
            .ToList();

    private sealed record Route(string Key, RouteEndpoint Endpoint);
}
