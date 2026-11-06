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

        builder.Property(i => i.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(i => i.VatRateApplied)
            .HasPrecision(9, 6);

        // The lengths match the customer and the company they are copied from, so a copy never overflows.
        builder.Property(i => i.CustomerName).HasMaxLength(200);
        builder.Property(i => i.CustomerAddress).HasMaxLength(500);
        builder.Property(i => i.CustomerVatNumber).HasMaxLength(20);
        builder.Property(i => i.SupplierCompanyName).HasMaxLength(200);
        builder.Property(i => i.SupplierTradingName).HasMaxLength(200);
        builder.Property(i => i.SupplierAddress).HasMaxLength(500);
        builder.Property(i => i.SupplierVatNumber).HasMaxLength(20);
        builder.Ignore(i => i.IssuedTo);
        builder.Ignore(i => i.IssuedBy);

        // Subtotal, VatTotal and Total are computed from Lines (see Invoice.cs), never
        // stored: persisting them would let them drift from the lines that produced them.
        // AddLine/RemoveLine reject anything past Draft, so those lines are frozen at issue.
        builder.Ignore(i => i.Subtotal);
        builder.Ignore(i => i.VatTotal);
        builder.Ignore(i => i.Total);

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
