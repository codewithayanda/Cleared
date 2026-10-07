using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class SignInThrottleRecordConfiguration : IEntityTypeConfiguration<SignInThrottleRecord>
{
    public void Configure(EntityTypeBuilder<SignInThrottleRecord> builder)
    {
        builder.ToTable("sign_in_throttles");

        // A SHA-256.
        builder.HasKey(t => t.EmailHash);
        builder.Property(t => t.EmailHash).HasMaxLength(32);
        builder.HasIndex(t => t.UpdatedAt);
    }
}
