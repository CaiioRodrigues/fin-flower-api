using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class RecurringItemConfiguration : IEntityTypeConfiguration<RecurringItem>
{
    public void Configure(EntityTypeBuilder<RecurringItem> builder)
    {
        builder.ToTable("RecurringItems");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Description).IsRequired().HasMaxLength(RecurringItem.MaxDescriptionLength);
        builder.Property(r => r.Category).IsRequired().HasMaxLength(RecurringItem.MaxCategoryLength);
        builder.Property(r => r.Notes).HasMaxLength(RecurringItem.MaxNotesLength);
        builder.Property(r => r.Kind).HasConversion<int>();
        builder.Property(r => r.Amount).HasPrecision(18, 2);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // A tela de fixos é sempre "do dono, deste tipo, os ativos primeiro".
        builder.HasIndex(r => new { r.OwnerId, r.Kind, r.IsActive });

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
