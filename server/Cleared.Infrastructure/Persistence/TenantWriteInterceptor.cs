using Cleared.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cleared.Infrastructure.Persistence;

// The write-side twin of the global query filters: reads are filtered, writes are verified.
// A row is tenant-owned when its type has a TenantId and a query filter, the same rule
// QueryFilterTests keeps complete. Throws before EF sends anything to the database.
public sealed class TenantWriteInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    private const string TenantIdProperty = "TenantId";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verify(eventData.Context);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Verify(eventData.Context);

        return ValueTask.FromResult(result);
    }

    private void Verify(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var rows = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(IsTenantOwned)
            .ToList();

        // Registration and login write only Tenant and ApplicationUser, so they never need a tenant.
        if (rows.Count == 0)
        {
            return;
        }

        var tenantId = CurrentTenantId(rows);
        var foreign = rows.Where(row => !BelongsTo(row, tenantId)).ToList();

        if (foreign.Count > 0)
        {
            throw new TenantMismatchException(
                $"Refusing to save {Describe(foreign)}: not owned by tenant {tenantId}.");
        }
    }

    private Guid CurrentTenantId(List<EntityEntry> rows)
    {
        try
        {
            return tenantContext.TenantId;
        }
        catch (InvalidOperationException exception)
        {
            throw new TenantMismatchException($"Refusing to save {Describe(rows)}: no tenant is in scope.", exception);
        }
    }

    private static bool IsTenantOwned(EntityEntry entry) =>
        entry.Metadata.FindProperty(TenantIdProperty) is not null
        && entry.Metadata.GetDeclaredQueryFilters().Count > 0;

    private static bool BelongsTo(EntityEntry row, Guid tenantId)
    {
        var property = row.Property(TenantIdProperty);

        // A row that moves between tenants fails on its stored value, not only its new one.
        return row.State == EntityState.Added
            ? Is(property.CurrentValue, tenantId)
            : Is(property.OriginalValue, tenantId) && Is(property.CurrentValue, tenantId);
    }

    private static bool Is(object? value, Guid tenantId) => value is Guid id && id == tenantId;

    private static string Describe(IEnumerable<EntityEntry> rows) =>
        string.Join(", ", rows.Select(row => $"{row.Metadata.ClrType.Name} ({row.State})"));
}
