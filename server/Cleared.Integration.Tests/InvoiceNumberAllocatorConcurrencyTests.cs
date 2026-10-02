using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Persistence;
using Cleared.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Integration.Tests;

// Runs against the throwaway Postgres container in ClearedApiFactory. Only a real database
// can show whether Postgres's UPSERT serialises concurrent claims on one (tenant_id, year) row.
[Collection(ApiCollection.Name)]
public class InvoiceNumberAllocatorConcurrencyTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task Issuing_50_invoices_concurrently_yields_contiguous_numbers()
    {
        // A fresh tenant id gives a fresh counter row, so tests sharing the database never collide.
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

    private ClearedDbContext CreateDbContext()
    {
        // Must match Program.cs's AddDbContext configuration, snake_case naming included.
        var options = new DbContextOptionsBuilder<ClearedDbContext>()
            .UseNpgsql(factory.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ClearedDbContext(options, new FixedTenantContext());
    }

    // The allocator writes raw SQL and never touches a query filter, so this is never read.
    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
