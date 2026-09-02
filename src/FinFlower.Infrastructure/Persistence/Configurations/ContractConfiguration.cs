using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Counterparty).IsRequired().HasMaxLength(Contract.MaxCounterpartyLength);
        builder.Property(c => c.Description).HasMaxLength(Contract.MaxDescriptionLength);
        builder.Property(c => c.TotalAmount).HasPrecision(18, 2);
        builder.Property(c => c.Direction).HasConversion<int>();
        builder.Property(c => c.PaymentMethod).HasConversion<int>();

        // Listagem e relatórios são sempre "do dono, por evento".
        builder.HasIndex(c => new { c.OwnerId, c.EventId });

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(c => c.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Installments)
            .WithOne()
            .HasForeignKey(i => i.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Installments)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_installments");

        builder.HasOne(c => c.Attachment)
            .WithOne()
            .HasForeignKey<ContractAttachment>(a => a.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}

public sealed class InstallmentConfiguration : IEntityTypeConfiguration<Installment>
{
    public void Configure(EntityTypeBuilder<Installment> builder)
    {
        builder.ToTable("Installments");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Amount).HasPrecision(18, 2);
        builder.Property(i => i.SettledAmount).HasPrecision(18, 2);
        builder.Property(i => i.Status).HasConversion<int>();

        // Não existem duas parcelas com o mesmo número no mesmo contrato.
        builder.HasIndex(i => new { i.ContractId, i.Number }).IsUnique();

        // O fluxo de caixa varre parcelas em aberto por vencimento.
        builder.HasIndex(i => new { i.Status, i.DueDate });
    }
}

public sealed class ContractAttachmentConfiguration : IEntityTypeConfiguration<ContractAttachment>
{
    public void Configure(EntityTypeBuilder<ContractAttachment> builder)
    {
        builder.ToTable("ContractAttachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.Content).IsRequired().HasColumnType("varbinary(max)");

        builder.HasIndex(a => a.ContractId).IsUnique();
    }
}
