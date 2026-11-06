using Cleared.Application.Abstractions;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Customers;

public sealed class CustomerService(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
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

    public async Task<CustomerResponse?> GetAsync(
        Guid tenantId, Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customerRepository.GetByIdAsync(tenantId, customerId, cancellationToken);

        return customer is null ? null : CustomerMapper.ToResponse(customer);
    }

    // Invoices that were issued already are not touched: each holds its own copy of the details.
    public async Task<CustomerResponse?> UpdateAsync(
        Guid tenantId, Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customerRepository.GetByIdAsync(tenantId, customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        customer.UpdateDetails(request.Name, request.VatNumber, request.Email, request.Address);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CustomerMapper.ToResponse(customer);
    }
}
