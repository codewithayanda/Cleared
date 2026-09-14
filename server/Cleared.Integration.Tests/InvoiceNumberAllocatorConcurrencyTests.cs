using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Cleared.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Cleared.Integration.Tests;

// Runs against a real Postgres: the local dev database via user secrets, or the ephemeral
// container ci.yml starts, via ConnectionStrings__Cleared. Only a real database can show
// whether Postgres's UPSERT serialises concurrent claims on one (tenant_id, year) row.
// TODO: writes to the dev database and never cleans up. Move to Testcontainers.
public class InvoiceNumberAllocatorConcurrencyTests : IAsyncLifetime
{
    private static readonly string _connectionString = LoadConnectionString();

    // Migrating here keeps the suite self-contained against a fresh database. Against the
    // dev database it is a no-op: EF skips anything already in __EFMigrationsHistory.
    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Issuing_50_invoices_concurrently_yields_contiguous_numbers()
    {
        // A fresh tenant id gives a fresh counter row, so the test repeats without a reset.
        var tenantId = Guid.NewGuid();
        const int year = 2026;
        const int concurrency = 50;

        // Task.WhenAll, not Parallel.ForEachAsync, which caps in-flight work at
        // ProcessorCount and would understate the contention. Each task gets its own
        // DbContext and connection, like 50 separate HTTP requests would.
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
        // Must match Program.cs's AddDbContext configuration. UseSnakeCaseNamingConvention
        // matters because MigrateAsync compares the compiled model against the migration
        // snapshot, and that snapshot came from the snake_case model.
        var options = new DbContextOptionsBuilder<ClearedDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ClearedDbContext(options, new FixedTenantContext());
    }

    // User secrets locally, environment variables in CI, same configuration key either way.
    // Environment variables are added last so CI's value wins on a conflict.
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

    // The allocator writes raw SQL and never touches a query filter, so this is never read.
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
