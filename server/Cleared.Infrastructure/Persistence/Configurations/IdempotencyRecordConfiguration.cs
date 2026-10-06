using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(r => new { r.TenantId, r.IdempotencyKey });

        // A SHA-256 in hex.
        builder.Property(r => r.Fingerprint).HasMaxLength(64);

        builder.HasIndex(r => new { r.TenantId, r.CreatedAt });
    }
}
