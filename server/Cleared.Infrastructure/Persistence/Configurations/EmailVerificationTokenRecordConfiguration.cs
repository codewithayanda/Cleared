using Cleared.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class EmailVerificationTokenRecordConfiguration : IEntityTypeConfiguration<EmailVerificationTokenRecord>
{
    public void Configure(EntityTypeBuilder<EmailVerificationTokenRecord> builder)
    {
        builder.ToTable("email_verification_tokens");
        builder.HasKey(t => t.Id);

        // A SHA-256.
        builder.Property(t => t.TokenHash).HasMaxLength(32);
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // The database itself allows one live link per person, however the requests arrive.
        builder.HasIndex(t => t.UserId).IsUnique().HasFilter("consumed_at IS NULL");

        // The count of links sent to a person lately, and the clearing of old ones.
        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
        builder.HasIndex(t => t.ExpiresAt);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
