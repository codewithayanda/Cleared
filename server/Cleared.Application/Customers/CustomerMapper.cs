using Cleared.Domain.Tenancy;

namespace Cleared.Application.Customers;

internal static class CustomerMapper
{
    public static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.TenantId, customer.Name, customer.VatNumber, customer.Email, customer.Address);
}
