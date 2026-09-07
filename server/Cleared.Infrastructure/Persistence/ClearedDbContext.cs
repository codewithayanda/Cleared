using Cleared.Domain.Invoicing;
using Cleared.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence;

public sealed class ClearedDbContext(DbContextOptions<ClearedDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClearedDbContext).Assembly);
    }
}
