using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tenants;

public sealed record RegisterTenantRequest(
    string CompanyName,
    VatStatus VatStatus,
    string? VatNumber,
    string? TradingName,
    string? Address = null,
    string? BankName = null,
    string? BankAccountNumber = null,
    string? BankBranchCode = null);

// Address and the banking fields are settled after sign-up via UpdateProfileAsync, not
// necessarily known at registration — see the "company profile" settings screen.
public sealed record UpdateTenantProfileRequest(
    string? Address, string? BankName, string? BankAccountNumber, string? BankBranchCode);

public sealed record TenantResponse(
    Guid Id,
    string CompanyName,
    string? TradingName,
    string VatStatus,
    string? VatNumber,
    string? Address,
    string? BankName,
    string? BankAccountNumber,
    string? BankBranchCode);
