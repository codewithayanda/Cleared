using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Identity;
using Cleared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// The repositories also filter by tenant explicitly, so the HTTP suite cannot tell whether
// the global query filters still work. These tests check the filters on their own, because
// they are the only protection for a query written without an explicit tenant predicate.
[Collection(ApiCollection.Name)]
public class QueryFilterTests(ClearedApiFactory factory)
{
    private static readonly IReadOnlyDictionary<Type, string> Unfiltered = new Dictionary<Type, string>
    {
        [typeof(ApplicationUser)] = "login looks the user up by email before any tenant is known",
        [typeof(InvoiceNumberSequence)] = "a counter reached only through raw SQL that names the tenant",
        [typeof(CreditNoteNumberSequence)] = "a counter reached only through raw SQL that names the tenant",
    };

    [Fact]
    public void Every_entity_with_a_TenantId_has_a_query_filter_or_a_recorded_reason_not_to()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();

        var unfiltered = dbContext.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("TenantId") is not null)
            .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
            .Select(entity => entity.ClrType)
            .Where(type => !Unfiltered.ContainsKey(type))
            .Select(type => type.Name)
            .ToList();

        Assert.True(
            unfiltered.Count == 0,
            $"No query filter on: {string.Join(", ", unfiltered)}. Add one in ClearedDbContext, " +
            "or add the type to Unfiltered with the reason.");
    }

    [Fact]
    public async Task The_filters_alone_hide_the_owners_rows_from_another_tenant()
    {
        var world = await IsolationWorld.CreateAsync(factory);

        var asOwner = await CountRowsAsync(world.Owner.TenantId);
        var asIntruder = await CountRowsAsync(world.Intruder.TenantId);

        // Control first: the rows exist, so zero for the intruder means they were hidden.
        Assert.True(asOwner.Values.All(count => count > 0), $"The owner should see rows: {Describe(asOwner)}");
        Assert.True(asIntruder.Values.All(count => count == 0), $"The intruder saw rows: {Describe(asIntruder)}");
    }

    // No Where clause on purpose: only the query filter is between the table and the caller.
    private async Task<Dictionary<string, int>> CountRowsAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ClearedDbContext>>();
        await using var dbContext = new ClearedDbContext(options, new FixedTenant(tenantId));

        return new Dictionary<string, int>
        {
            ["customers"] = await dbContext.Customers.CountAsync(),
            ["invoices"] = await dbContext.Invoices.CountAsync(),
            ["credit notes"] = await dbContext.CreditNotes.CountAsync(),
            ["payments"] = await dbContext.Payments.CountAsync(),
            ["audit entries"] = await dbContext.AuditLogs.CountAsync(),
        };
    }

    private static string Describe(Dictionary<string, int> counts) =>
        string.Join(", ", counts.Select(pair => $"{pair.Key}={pair.Value}"));

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }
}
