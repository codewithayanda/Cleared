using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// The fallback policy turns "forgot [Authorize]" from an open endpoint into a 401.
[Collection(ApiCollection.Name)]
public class DefaultDenyTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task An_endpoint_with_no_authorization_metadata_still_needs_a_signed_in_user()
    {
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Health_probes_answer_without_a_token(string path)
    {
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // OpenAPI and Scalar only exist in Development, and the fallback policy would lock them.
    [Fact]
    public async Task The_development_api_docs_stay_public()
    {
        using var development = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
