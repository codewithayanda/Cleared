using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("credit_notes");

        builder.HasKey(cn => cn.Id);

        builder.Property(cn => cn.Number)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(cn => cn.Reason)
            .IsRequired()
            .HasMaxLength(500);

        // Subtotal/VatTotal/Total are computed live from Lines — never stored, same
        // reasoning as Invoice (see InvoiceConfiguration).
        builder.Ignore(cn => cn.Subtotal);
        builder.Ignore(cn => cn.VatTotal);
        builder.Ignore(cn => cn.Total);

        builder.HasMany(cn => cn.Lines)
            .WithOne()
            .HasForeignKey(l => l.CreditNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(cn => cn.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(cn => new { cn.TenantId, cn.InvoiceId });
        builder.HasIndex(cn => new { cn.TenantId, cn.Number }).IsUnique();
    }
}
