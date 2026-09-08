using Cleared.Domain.Tenancy;

namespace Cleared.Application.Abstractions;

public interface ICustomerRepository
{
    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task<Customer?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Customer>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
}
