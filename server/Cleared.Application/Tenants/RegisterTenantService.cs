using Cleared.Application.Abstractions;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tenants;

public sealed class RegisterTenantService(ITenantRepository tenantRepository, IUnitOfWork unitOfWork)
{
    public async Task<TenantResponse> RegisterAsync(RegisterTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = Tenant.Register(
            Guid.NewGuid(), request.CompanyName, request.VatStatus, request.VatNumber,
            DateTimeOffset.UtcNow, request.TradingName, request.Address,
            request.BankName, request.BankAccountNumber, request.BankBranchCode);

        await tenantRepository.AddAsync(tenant, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TenantMapper.ToResponse(tenant);
    }

    public async Task<TenantResponse?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken);

        return tenant is null ? null : TenantMapper.ToResponse(tenant);
    }

    public async Task<TenantResponse?> UpdateProfileAsync(
        Guid tenantId, UpdateTenantProfileRequest request, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        tenant.UpdateProfile(request.Address, request.BankName, request.BankAccountNumber, request.BankBranchCode);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TenantMapper.ToResponse(tenant);
    }
}
