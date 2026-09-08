using Cleared.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class CreditNoteLineItemConfiguration : IEntityTypeConfiguration<CreditNoteLineItem>
{
    public void Configure(EntityTypeBuilder<CreditNoteLineItem> builder)
    {
        builder.ToTable("credit_note_line_items");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(l => l.Quantity)
            .HasPrecision(18, 4);

        builder.Property(l => l.VatTreatment)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.ComplexProperty(l => l.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasPrecision(18, 2);
            money.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        });

        builder.ComplexProperty(l => l.LineSubtotal, money =>
        {
            money.Property(m => m.Amount).HasPrecision(18, 2);
            money.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        });

        builder.ComplexProperty(l => l.LineVat, money =>
        {
            money.Property(m => m.Amount).HasPrecision(18, 2);
            money.Property(m => m.Currency).HasConversion<string>().HasMaxLength(3);
        });

        builder.HasIndex(l => l.InvoiceLineItemId);
    }
}
