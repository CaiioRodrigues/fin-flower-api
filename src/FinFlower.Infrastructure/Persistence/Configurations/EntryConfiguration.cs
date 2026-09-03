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
        builder.Property(e => e.Source).HasConversion<int>();

        // decimal(18,2): dinheiro nunca em float — arredondamento binário corrompe centavos.
        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // O evento é opcional e não manda no lançamento: apagar um evento não
        // pode levar dinheiro junto, então nada de cascata aqui.
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(e => e.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RecurringItem>()
            .WithMany()
            .HasForeignKey(e => e.RecurringItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // O índice do livro-caixa: "do dono, do mais recente para o mais antigo".
        builder.HasIndex(e => new { e.OwnerId, e.OccurredOn });
        builder.HasIndex(e => new { e.EventId, e.OccurredOn });

        // Uma competência de um item fixo só pode existir uma vez. É esta chave
        // que torna "gerar o mês" idempotente de verdade: mesmo com duas
        // requisições simultâneas, a segunda esbarra no banco, não na consulta.
        builder.HasIndex(e => new { e.RecurringItemId, e.RecurringMonth })
            .IsUnique()
            .HasFilter("[RecurringItemId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(e => e.InstallmentId)
            .IsUnique()
            .HasFilter("[InstallmentId] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
