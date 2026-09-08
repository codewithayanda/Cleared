using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Cleared.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Cleared.Integration.Tests;

// Runs against a real Postgres (Testcontainers was cut from this project's scope):
// locally, the same dev database Cleared.API talks to, via the same user secret;
// in CI, the ephemeral postgres service container ci.yml starts for this job, via
// ConnectionStrings__Cleared. There is nothing to mock here — the property under test
// is whether Postgres's own UPSERT serialises concurrent claims on one (tenant_id, year)
// row, and that can only be proven against a real database with real concurrent
// connections.
public class InvoiceNumberAllocatorConcurrencyTests : IAsyncLifetime
{
    private static readonly string _connectionString = LoadConnectionString();

    // Applying migrations here rather than requiring a pre-provisioned schema means this
    // suite is self-contained against a fresh database — exactly what CI's disposable
    // container is. Against the local dev database it's a harmless no-op: EF checks
    // __EFMigrationsHistory and skips anything already applied.
    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Issuing_50_invoices_concurrently_yields_contiguous_numbers()
    {
        // A fresh tenant id means a fresh counter row, so the test is repeatable
        // without needing to reset any shared state between runs.
        var tenantId = Guid.NewGuid();
        const int year = 2026;
        const int concurrency = 50;

        // Task.WhenAll, not Parallel.ForEachAsync: the latter caps in-flight work at
        // Environment.ProcessorCount by default, which would understate the real
        // contention. Each task gets its own DbContext/connection, mirroring how 50
        // separate HTTP requests would each get their own scoped context.
        var allocations = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await using var dbContext = CreateDbContext();
            var allocator = new InvoiceNumberAllocator(dbContext);

            return await allocator.AllocateAsync(tenantId, year, CancellationToken.None);
        });

        var invoiceNumbers = await Task.WhenAll(allocations);

        Assert.All(invoiceNumbers, n => Assert.StartsWith($"INV-{year}-", n, StringComparison.Ordinal));

        var claimedNumbers = invoiceNumbers.Select(ParseSequenceNumber).OrderBy(n => n).ToList();

        Assert.Equal(concurrency, claimedNumbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, concurrency), claimedNumbers);
    }

    private static int ParseSequenceNumber(string invoiceNumber) => int.Parse(invoiceNumber.Split('-')[^1]);

    private static ClearedDbContext CreateDbContext()
    {
        // Must match Program.cs's own AddDbContext configuration exactly. Missing
        // UseSnakeCaseNamingConvention() here never mattered while this DbContext was
        // only ever used for raw SQL (bypassing EF's model layer entirely) — but
        // MigrateAsync() compares the compiled model against the migration snapshot,
        // and that snapshot was generated from the snake_case-configured model.
        var options = new DbContextOptionsBuilder<ClearedDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ClearedDbContext(options, new FixedTenantContext());
    }

    // User secrets for local dev, environment variables for CI — same configuration key
    // either way, so nothing in the test itself needs to know which one it's running
    // under. In practice only one source ever has a value: a developer's machine has the
    // user secret but no such environment variable, ci.yml sets the environment variable
    // but the runner has no secrets.json. Later providers override earlier ones on a
    // genuine conflict, which is why environment variables are added last — CI's value
    // should always win there, never get shadowed by a stray same-named variable.
    private static string LoadConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("41d0c632-5dba-4899-90a7-a974ced4d66d")
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString("Cleared") ?? throw new InvalidOperationException(
            "No ConnectionStrings:Cleared found. Locally, set it the same way you did for " +
            "Cleared.API (these tests read the same secrets store by UserSecretsId). In CI, " +
            "it comes from the ConnectionStrings__Cleared environment variable in ci.yml.");
    }

    // The allocator writes raw SQL to invoice_number_sequences directly; it never
    // touches a tenant query filter, so the value here is never actually read.
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
