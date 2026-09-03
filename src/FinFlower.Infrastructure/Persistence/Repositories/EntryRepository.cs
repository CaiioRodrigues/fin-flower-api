using FinFlower.Application.Abstractions;
using FinFlower.Domain.Entities;
using FinFlower.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class EntryRepository(AppDbContext context) : IEntryRepository
{
    public Task<Entry?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        // O filtro por dono faz parte da consulta, não de uma checagem posterior:
        // não existe caminho que carregue o lançamento de outra pessoa.
        context.Entries.FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId, cancellationToken);

    public Task<Entry?> GetByInstallmentAsync(
        Guid installmentId,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        context.Entries.FirstOrDefaultAsync(
            e => e.InstallmentId == installmentId && e.OwnerId == ownerId,
            cancellationToken);

    public async Task<IReadOnlySet<(Guid RecurringItemId, DateOnly Month)>> GetGeneratedRecurringMonthsAsync(
        Guid ownerId,
        YearMonth competence,
        CancellationToken cancellationToken = default)
    {
        var month = competence.FirstDay;

        var rows = await context.Entries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.RecurringMonth == month && e.RecurringItemId != null)
            .Select(e => e.RecurringItemId!.Value)
            .ToListAsync(cancellationToken);

        return rows.Select(id => (id, month)).ToHashSet();
    }

    public async Task<IReadOnlyList<Entry>> ListByEventAsync(
        Guid eventId,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await context.Entries
            .Where(e => e.EventId == eventId && e.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

    public void Add(Entry entry) => context.Entries.Add(entry);

    public void AddRange(IEnumerable<Entry> entries) => context.Entries.AddRange(entries);
}
