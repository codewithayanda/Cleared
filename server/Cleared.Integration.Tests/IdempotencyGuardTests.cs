using Cleared.API.Idempotency;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// A write that can be repeated must carry an Idempotency-Key. Fails when a new write endpoint has
// neither the key nor a recorded reason to be exempt, so a new route cannot skip the protection.
[Collection(ApiCollection.Name)]
public class IdempotencyGuardTests(ClearedApiFactory factory)
{
    private static readonly string[] WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        ["POST api/v1/auth/register"] = "creates its own tenant, and a repeat fails on the unique email",
        ["POST api/v1/auth/login"] = "changes nothing a repeat could duplicate",
        ["POST api/v1/auth/refresh"] = "single use by design, a repeat is read as a stolen token and ends the session",
        ["POST api/v1/auth/logout"] = "ending a session that has ended changes nothing",
        ["PUT api/v1/tenants/me"] = "a PUT, so repeating it has the same effect as doing it once",
    };

    [Fact]
    public void Every_write_endpoint_takes_an_idempotency_key_or_is_exempt_with_a_reason()
    {
        var unprotected = WriteEndpoints()
            .Where(write => !Exempt.ContainsKey(write.Key))
            .Where(write => !TakesKey(write.Action))
            .Select(write => write.Key)
            .ToList();

        Assert.True(
            unprotected.Count == 0,
            $"No Idempotency-Key on: {string.Join(", ", unprotected)}. Take the header and run the work " +
            "through IdempotentExecutor, or add the endpoint to Exempt with the reason.");
    }

    [Fact]
    public void The_exemptions_name_only_write_endpoints_that_exist()
    {
        var existing = WriteEndpoints().Select(write => write.Key).ToHashSet();

        var stale = Exempt.Keys.Where(key => !existing.Contains(key)).ToList();

        Assert.True(stale.Count == 0, $"No such write endpoint: {string.Join(", ", stale)}. Remove or rename it.");
    }

    private static bool TakesKey(ControllerActionDescriptor action) =>
        action.Parameters.Any(parameter =>
            parameter.BindingInfo?.BindingSource == BindingSource.Header
            && parameter.BindingInfo.BinderModelName == IdempotencyHeader.Name);

    // Read from the routing table of the running API, so nothing can be added without showing up.
    private List<(string Key, ControllerActionDescriptor Action)> WriteEndpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => WriteMethods.Contains(method))
                .Select(method => (
                    Key: $"{method} {endpoint.RoutePattern.RawText!.TrimStart('/')}",
                    Action: endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!)))
            .ToList();
}
