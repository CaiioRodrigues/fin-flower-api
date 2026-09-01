using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(Event.MaxNameLength);
        builder.Property(e => e.Description).HasMaxLength(Event.MaxDescriptionLength);
        builder.Property(e => e.Status).HasConversion<int>();

        // Toda listagem de evento é "do dono, ordenada por data".
        builder.HasIndex(e => new { e.OwnerId, e.EventDate });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Entries)
            .WithOne()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Entries)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_entries");

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
