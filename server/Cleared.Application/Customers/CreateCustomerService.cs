using Cleared.Application.Abstractions;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Customers;

public sealed class CreateCustomerService(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
{
    public async Task<CustomerResponse> CreateAsync(
        Guid tenantId, CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = Customer.Create(
            Guid.NewGuid(), tenantId, request.Name, DateTimeOffset.UtcNow,
            request.VatNumber, request.Email, request.Address);

        await customerRepository.AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CustomerMapper.ToResponse(customer);
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var customers = await customerRepository.ListAsync(tenantId, cancellationToken);

        return customers.Select(CustomerMapper.ToResponse).ToList();
    }
}
