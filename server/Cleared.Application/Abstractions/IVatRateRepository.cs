using Cleared.Domain.Invoicing;

namespace Cleared.Application.Abstractions;

public interface IVatRateRepository
{
    Task<VatRate?> GetEffectiveRateAsync(DateOnly date, CancellationToken cancellationToken);
}
