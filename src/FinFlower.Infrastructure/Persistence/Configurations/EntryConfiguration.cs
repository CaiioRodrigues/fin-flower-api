using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("Entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Description).IsRequired().HasMaxLength(Entry.MaxDescriptionLength);
        builder.Property(e => e.Category).IsRequired().HasMaxLength(Entry.MaxCategoryLength);
        builder.Property(e => e.Type).HasConversion<int>();

        // decimal(18,2): dinheiro nunca em float — arredondamento binário corrompe centavos.
        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.EventId, e.OccurredOn });

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
