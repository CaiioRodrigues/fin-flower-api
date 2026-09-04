using FinFlower.Application.Common;
using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FinFlower.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IDateTimeProvider clock)
    : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<CashOpening> CashOpenings => Set<CashOpening>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<ContractAttachment> ContractAttachments => Set<ContractAttachment>();
    public DbSet<RecurringItem> RecurringItems => Set<RecurringItem>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // As entidades geram o próprio Id (Guid v7) no construtor. Sem isto o EF
        // trata a chave já preenchida como "registro existente" e marca uma
        // entidade nova como Modified — o insert vira um update que falha.
        foreach (var key in modelBuilder.Model.GetEntityTypes()
                     .Select(entity => entity.FindProperty(nameof(Entity.Id)))
                     .Where(property => property is not null))
        {
            key!.ValueGenerated = ValueGenerated.Never;
        }

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Preenche a auditoria em um único lugar, para nenhum caso de uso esquecer.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                // Escrita pela API do EF: o domínio mantém os setters privados
                // e a auditoria não vira um buraco na encapsulação.
                case EntityState.Added:
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAt).CurrentValue = now;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveChangesAsync(cancellationToken);
}
