namespace Cleared.Application.Customers;

public sealed record UpdateCustomerRequest(string Name, string? VatNumber, string? Email, string? Address);
