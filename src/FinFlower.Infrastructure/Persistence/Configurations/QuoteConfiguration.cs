using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("Quotes");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.Number).IsRequired().HasMaxLength(Quote.MaxNumberLength);
        builder.Property(q => q.ClientName).IsRequired().HasMaxLength(Quote.MaxClientLength);
        builder.Property(q => q.Title).IsRequired().HasMaxLength(Quote.MaxTitleLength);
        builder.Property(q => q.Notes).HasMaxLength(Quote.MaxNotesLength);
        builder.Property(q => q.Status).HasConversion<int>();
        builder.Property(q => q.DiscountAmount).HasPrecision(18, 2);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(q => q.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(q => q.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        // O número é o que o cliente vê e cita: dois iguais no mesmo dono seria
        // um erro de operação, não um detalhe.
        builder.HasIndex(q => new { q.OwnerId, q.Number })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(q => new { q.OwnerId, q.IssuedOn });

        builder.HasMany(q => q.Items)
            .WithOne()
            .HasForeignKey(i => i.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(q => q.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_items");

        // OrderedItems é a mesma coleção, só ordenada para leitura. Sem isto o
        // EF a descobre como uma segunda navegação e cria uma chave estrangeira
        // duplicada — QuoteId1 — apontando para a mesma tabela.
        builder.Ignore(q => q.OrderedItems);

        builder.HasQueryFilter(q => !q.IsDeleted);
    }
}

public sealed class QuoteItemConfiguration : IEntityTypeConfiguration<QuoteItem>
{
    public void Configure(EntityTypeBuilder<QuoteItem> builder)
    {
        builder.ToTable("QuoteItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Description).IsRequired().HasMaxLength(QuoteItem.MaxDescriptionLength);
        builder.Property(i => i.Unit).HasMaxLength(QuoteItem.MaxUnitLength);

        // Quantidade com três casas: meia diária e um terço de hora existem.
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);

        builder.HasIndex(i => new { i.QuoteId, i.Position });

        // Total é calculado a partir de quantidade e preço: guardá-lo abriria a
        // porta para a soma divergir das linhas.
        builder.Ignore(i => i.Total);
    }
}
