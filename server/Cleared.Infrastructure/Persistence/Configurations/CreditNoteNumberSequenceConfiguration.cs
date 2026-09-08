using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cleared.Infrastructure.Persistence.Configurations;

public sealed class CreditNoteNumberSequenceConfiguration : IEntityTypeConfiguration<CreditNoteNumberSequence>
{
    public void Configure(EntityTypeBuilder<CreditNoteNumberSequence> builder)
    {
        builder.ToTable("credit_note_number_sequences");
        builder.HasKey(s => new { s.TenantId, s.Year });
    }
}
