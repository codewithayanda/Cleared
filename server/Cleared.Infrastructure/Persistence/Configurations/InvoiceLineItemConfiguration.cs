using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.ToTable("invoice_line_items");

        builder.HasKey(li => li.Id);

        builder.Property(li => li.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(li => li.Quantity)
            .HasPrecision(18, 4);

        builder.ComplexProperty(li => li.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasPrecision(18, 2);
            money.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        });

        builder.ComplexProperty(li => li.LineSubtotal, money =>
        {
            money.Property(m => m.Amount).HasPrecision(18, 2);
            money.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        });
    }
}
