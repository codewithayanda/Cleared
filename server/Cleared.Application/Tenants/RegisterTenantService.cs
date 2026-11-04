using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tenants;

public sealed class RegisterTenantService(
    ITenantRepository tenantRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    IEmailVerificationService emailVerification)
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

        // Where an invoice says to pay is the one detail worth stealing, so it changes only for an account
        // whose email has been confirmed. Changing the address alone is not held back.
        if (ChangesBankDetails(tenant, request) && !await emailVerification.IsVerifiedAsync(currentUser.UserId, cancellationToken))
        {
            throw new EmailNotVerifiedException("Banking details can be changed only after you confirm your email address.");
        }

        tenant.UpdateProfile(request.Address, request.BankName, request.BankAccountNumber, request.BankBranchCode);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TenantMapper.ToResponse(tenant);
    }

    private static bool ChangesBankDetails(Tenant tenant, UpdateTenantProfileRequest request) =>
        Differs(tenant.BankName, request.BankName)
        || Differs(tenant.BankAccountNumber, request.BankAccountNumber)
        || Differs(tenant.BankBranchCode, request.BankBranchCode);

    // Nothing, an empty box and spaces all mean the same, so none of them counts as a change.
    private static bool Differs(string? stored, string? requested) =>
        !string.Equals(Trimmed(stored), Trimmed(requested), StringComparison.Ordinal);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
