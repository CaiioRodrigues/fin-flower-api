using FinFlower.Application.Abstractions;
using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Queries;

/// <summary>
/// Leitura do livro-caixa. O mês a mês é uma consulta agrupada — o banco reduz
/// milhares de lançamentos a algumas dezenas de linhas, e a composição do
/// relatório acontece sobre esse conjunto pequeno.
/// </summary>
public sealed class EntryQueries(AppDbContext context) : IEntryQueries
{
    public async Task<LedgerPageResponse> ListAsync(
        Guid ownerId,
        EntryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Filtered(ownerId, filter);

        // Os totais são do filtro inteiro, não da página: quem olha o mês quer
        // o saldo do mês, ainda que esteja vendo as cinquenta primeiras linhas.
        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Income = g.Where(e => e.Type == EntryType.Income).Sum(e => (decimal?)e.Amount) ?? 0m,
                Expense = g.Where(e => e.Type == EntryType.Expense).Sum(e => (decimal?)e.Amount) ?? 0m,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await LedgerProjection
            .Project(
                query.OrderByDescending(e => e.OccurredOn).ThenByDescending(e => e.CreatedAt),
                context.Events.AsNoTracking())
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var income = totals?.Income ?? 0m;
        var expense = totals?.Expense ?? 0m;

        return new LedgerPageResponse(
            [.. rows.Select(LedgerProjection.ToResponse)],
            totals?.Count ?? 0,
            page,
            pageSize,
            income,
            expense,
            income - expense);
    }

    public async Task<LedgerEntryResponse?> GetAsync(
        Guid entryId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var row = await LedgerProjection
            .Project(
                context.Entries.AsNoTracking().Where(e => e.Id == entryId && e.OwnerId == ownerId),
                context.Events.AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        return row?.ToResponse();
    }

    public async Task<IReadOnlyList<MonthlyBucket>> GetMonthlyBucketsAsync(
        Guid ownerId,
        YearMonth from,
        YearMonth to,
        CancellationToken cancellationToken = default)
    {
        var start = from.FirstDay;
        var end = to.LastDay;

        // A junção à esquerda com o item fixo é o que permite responder "quanto
        // do mês é pró-labore" sem copiar o tipo para dentro do lançamento —
        // duplicá-lo abriria a porta para os dois divergirem.
        var rows = await (
                from entry in context.Entries.AsNoTracking()
                where entry.OwnerId == ownerId
                      && entry.OccurredOn >= start
                      && entry.OccurredOn <= end
                join item in context.RecurringItems.AsNoTracking()
                    on entry.RecurringItemId equals item.Id into recurring
                from item in recurring.DefaultIfEmpty()
                group new { entry.Amount } by new
                {
                    entry.OccurredOn.Year,
                    entry.OccurredOn.Month,
                    entry.Type,
                    entry.Category,
                    entry.Source,
                    Kind = (RecurringKind?)item.Kind,
                    HasEvent = entry.EventId != null,
                }
                into grouped
                select new MonthlyBucket(
                    grouped.Key.Year,
                    grouped.Key.Month,
                    grouped.Key.Type,
                    grouped.Key.Category,
                    grouped.Key.Source,
                    grouped.Key.Kind,
                    grouped.Key.HasEvent,
                    grouped.Sum(x => x.Amount),
                    grouped.Count()))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<decimal> GetBalanceBeforeAsync(
        Guid ownerId,
        YearMonth competence,
        CancellationToken cancellationToken = default)
    {
        var start = competence.FirstDay;

        return await context.Entries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.OccurredOn < start)
            .SumAsync(e => (decimal?)(e.Type == EntryType.Income ? e.Amount : -e.Amount), cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await context.Entries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId)
            .Select(e => e.Category)
            .Distinct()
            .OrderBy(category => category)
            .ToListAsync(cancellationToken);

    private IQueryable<Entry> Filtered(Guid ownerId, EntryFilter filter)
    {
        var query = context.Entries.AsNoTracking().Where(e => e.OwnerId == ownerId);

        if (filter.From is { } from) query = query.Where(e => e.OccurredOn >= from);
        if (filter.To is { } to) query = query.Where(e => e.OccurredOn <= to);
        if (filter.Type is { } type) query = query.Where(e => e.Type == type);
        if (filter.Source is { } source) query = query.Where(e => e.Source == source);
        if (filter.EventId is { } eventId) query = query.Where(e => e.EventId == eventId);

        if (filter.WithoutEvent is { } withoutEvent)
        {
            query = withoutEvent
                ? query.Where(e => e.EventId == null)
                : query.Where(e => e.EventId != null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var category = filter.Category.Trim();
            query = query.Where(e => e.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e => e.Description.Contains(term) || e.Category.Contains(term));
        }

        return query;
    }
}
