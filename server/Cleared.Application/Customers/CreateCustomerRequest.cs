namespace Cleared.Application.Customers;

public sealed record CreateCustomerRequest(string Name, string? VatNumber, string? Email);

public sealed record CustomerResponse(Guid Id, Guid TenantId, string Name, string? VatNumber, string? Email);
