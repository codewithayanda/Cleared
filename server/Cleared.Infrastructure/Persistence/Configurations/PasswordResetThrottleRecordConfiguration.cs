using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class PasswordResetThrottleRecordConfiguration : IEntityTypeConfiguration<PasswordResetThrottleRecord>
{
    public void Configure(EntityTypeBuilder<PasswordResetThrottleRecord> builder)
    {
        builder.ToTable("password_reset_throttles");

        // A SHA-256.
        builder.HasKey(t => t.EmailHash);
        builder.Property(t => t.EmailHash).HasMaxLength(32);
        builder.HasIndex(t => t.WindowStart);
    }
}
