using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tenants;

internal static class TenantMapper
{
    public static TenantResponse ToResponse(Tenant tenant) => new(
        tenant.Id, tenant.CompanyName, tenant.TradingName, tenant.VatStatus.ToString(), tenant.VatNumber);
}
