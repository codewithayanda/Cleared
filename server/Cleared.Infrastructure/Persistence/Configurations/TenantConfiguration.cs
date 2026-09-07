using Cleared.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.CompanyName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.TradingName)
            .HasMaxLength(200);

        builder.Property(t => t.VatNumber)
            .HasMaxLength(20);

        builder.Property(t => t.VatStatus)
            .HasConversion<string>()
            .HasMaxLength(20);
    }
}
