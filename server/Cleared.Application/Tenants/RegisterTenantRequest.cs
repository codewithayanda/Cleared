using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tenants;

public sealed record RegisterTenantRequest(
    string CompanyName, VatStatus VatStatus, string? VatNumber, string? TradingName);

public sealed record TenantResponse(
    Guid Id, string CompanyName, string? TradingName, string VatStatus, string? VatNumber);
