using Cleared.Application.Abstractions;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tenants;

public sealed class RegisterTenantService(ITenantRepository tenantRepository, IUnitOfWork unitOfWork)
{
    public async Task<TenantResponse> RegisterAsync(RegisterTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = Tenant.Register(
            Guid.NewGuid(), request.CompanyName, request.VatStatus, request.VatNumber,
            DateTimeOffset.UtcNow, request.TradingName);

        await tenantRepository.AddAsync(tenant, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToResponse(tenant);
    }

    private static TenantResponse ToResponse(Tenant tenant) => new(
        tenant.Id, tenant.CompanyName, tenant.TradingName, tenant.VatStatus.ToString(), tenant.VatNumber);
}
