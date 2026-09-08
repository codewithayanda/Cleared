using Cleared.Application.Abstractions;
using Cleared.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository(ClearedDbContext dbContext) : ICustomerRepository
{
    public async Task AddAsync(Customer customer, CancellationToken cancellationToken) =>
        await dbContext.Customers.AddAsync(customer, cancellationToken);

    public async Task<Customer?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken) =>
        await dbContext.Customers.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.Id == customerId, cancellationToken);

    public async Task<IReadOnlyList<Customer>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await dbContext.Customers.Where(c => c.TenantId == tenantId).ToListAsync(cancellationToken);
}
