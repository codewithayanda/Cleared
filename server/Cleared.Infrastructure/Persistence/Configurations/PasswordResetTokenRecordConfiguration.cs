using Cleared.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class PasswordResetTokenRecordConfiguration : IEntityTypeConfiguration<PasswordResetTokenRecord>
{
    public void Configure(EntityTypeBuilder<PasswordResetTokenRecord> builder)
    {
        builder.ToTable("password_reset_tokens");
        builder.HasKey(t => t.Id);

        // A SHA-256.
        builder.Property(t => t.TokenHash).HasMaxLength(32);
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // The database itself allows one live link per person, however the requests arrive.
        builder.HasIndex(t => t.UserId).IsUnique().HasFilter("consumed_at IS NULL");
        builder.HasIndex(t => t.ExpiresAt);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
