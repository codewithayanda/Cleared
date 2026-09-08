using Cleared.Application.Abstractions;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Tenancy;
using Cleared.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence;

public sealed class ClearedDbContext(DbContextOptions<ClearedDbContext> options, ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    // Not tenant-scoped — national reference data, shared by every tenant. See the ERD's
    // note on the few tables that are deliberately not filtered this way.
    public DbSet<VatRate> VatRates => Set<VatRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClearedDbContext).Assembly);
        modelBuilder.Entity<Customer>().HasQueryFilter(c => c.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<Invoice>().HasQueryFilter(i => i.TenantId == tenantContext.TenantId);
        modelBuilder.Entity<CreditNote>().HasQueryFilter(cn => cn.TenantId == tenantContext.TenantId);
    }
}
