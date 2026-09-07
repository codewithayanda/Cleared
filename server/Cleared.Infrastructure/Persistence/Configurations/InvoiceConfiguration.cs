using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(i => i.Number)
            .HasMaxLength(30);

        // Subtotal is computed live from Lines (see Invoice.cs) — never stored.
        // Persisting it would let it drift from the lines that produced it (I-02/I-03).
        builder.Ignore(i => i.Subtotal);

        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(li => li.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.CustomerId });
        builder.HasIndex(i => new { i.TenantId, i.DueDate });
    }
}
