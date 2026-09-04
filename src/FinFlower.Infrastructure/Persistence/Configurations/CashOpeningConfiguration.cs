using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class CashOpeningConfiguration : IEntityTypeConfiguration<CashOpening>
{
    public void Configure(EntityTypeBuilder<CashOpening> builder)
    {
        builder.ToTable("CashOpenings");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Amount).HasPrecision(18, 2);
        builder.Property(o => o.Notes).HasMaxLength(CashOpening.MaxNotesLength);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Um saldo inicial por dono, garantido pelo banco: dois deles se somariam
        // em silêncio e o saldo da tela passaria a mentir sem nenhum sintoma.
        builder.HasIndex(o => o.OwnerId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasQueryFilter(o => !o.IsDeleted);
    }
}
