using Cleared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Cleared.Integration.Tests;

// One throwaway Postgres container and one running API for the whole test run, shared
// through ApiCollection. Tests isolate themselves by creating their own tenant.
public sealed class ClearedApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The tests mint tokens of their own, so they need to know the key the API runs with. It is
    // over 64 bytes so a test can also sign with HS512, which refuses shorter keys.
    public const string SigningKey = "integration-tests-only-signing-key-0123456789-0123456789-0123456789-0123456789";

    private static readonly string[] RateLimitRules = ["Login", "Register", "Session", "Api"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    // "localhost" resolves to IPv6 first and stalls against the container's mapped port here.
    public string ConnectionString => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
    {
        Host = _postgres.Hostname == "localhost" ? "127.0.0.1" : _postgres.Hostname,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();

        // Configuration precedence decides which database the app really uses. Refuse to
        // migrate or touch anything unless it is the container.
        var actual = new NpgsqlConnectionStringBuilder(dbContext.Database.GetConnectionString()).ConnectionString;
        if (actual != ConnectionString)
        {
            throw new InvalidOperationException("The API is not pointed at the test container. Refusing to run.");
        }

        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Cleared", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", SigningKey);

        // The suite registers hundreds of tenants from one address, so this copy is all but unlimited.
        // RateLimitTests starts copies with small limits.
        foreach (var rule in RateLimitRules)
        {
            builder.UseSetting($"RateLimits:{rule}:PermitLimit", "1000000");
        }
    }
}
