using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Cleared.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Cleared.Integration.Tests;

// Runs against the real local Postgres (Testcontainers was cut from this project's
// scope), reusing the same ConnectionStrings:Cleared user secret Cleared.API already
// has configured. There is nothing to mock here — the property under test is whether
// Postgres's own UPSERT serialises concurrent claims on one (tenant_id, year) row, and
// that can only be proven against a real database with real concurrent connections.
public class InvoiceNumberAllocatorConcurrencyTests
{
    private static readonly string _connectionString = LoadConnectionString();

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
        var options = new DbContextOptionsBuilder<ClearedDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new ClearedDbContext(options, new FixedTenantContext());
    }

    private static string LoadConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("41d0c632-5dba-4899-90a7-a974ced4d66d")
            .Build();

        return configuration.GetConnectionString("Cleared") ?? throw new InvalidOperationException(
            "No ConnectionStrings:Cleared user secret found. Set it the same way you did for " +
            "Cleared.API — these tests read the same secrets store by UserSecretsId.");
    }

    // The allocator writes raw SQL to invoice_number_sequences directly; it never
    // touches a tenant query filter, so the value here is never actually read.
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
