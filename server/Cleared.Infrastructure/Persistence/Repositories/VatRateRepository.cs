using Cleared.Application.Abstractions;
using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace Cleared.Infrastructure.Persistence.Repositories;

public sealed class VatRateRepository(ClearedDbContext dbContext) : IVatRateRepository
{
    public async Task<VatRate?> GetEffectiveRateAsync(DateOnly date, CancellationToken cancellationToken) =>
        await dbContext.VatRates
            .Where(r => r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date))
            .SingleOrDefaultAsync(cancellationToken);
}
