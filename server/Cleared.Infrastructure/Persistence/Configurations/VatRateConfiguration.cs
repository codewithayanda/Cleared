using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class VatRateConfiguration : IEntityTypeConfiguration<VatRate>
{
    public void Configure(EntityTypeBuilder<VatRate> builder)
    {
        builder.ToTable("vat_rates");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rate)
            .HasPrecision(9, 6);

        // Not tenant-scoped — this is national reference data, one of the few tables in
        // the schema without a tenant_id (see the ERD's note on that).
        builder.HasIndex(r => r.EffectiveFrom);
    }
}
