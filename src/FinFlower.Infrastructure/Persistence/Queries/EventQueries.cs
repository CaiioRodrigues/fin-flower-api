using FinFlower.Application.Abstractions;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Reports.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Queries;

/// <summary>
/// Consultas de leitura de evento. Os lançamentos já não vivem dentro do evento,
/// então os totais vêm de subconsultas correlacionadas sobre <c>Entries</c> — o
/// banco agrega e devolve os números, em vez de trazer todo o movimento para cá.
/// </summary>
public sealed class EventQueries(AppDbContext context) : IEventQueries
{
    /// <summary>Totais de um evento, no formato em que o banco os devolve.</summary>
    private sealed record Totals(
        Guid Id,
        string Name,
        string? Description,
        DateOnly EventDate,
        EventStatus Status,
        decimal Income,
        decimal Expense,
        int EntryCount)
    {
        public decimal Result => Income - Expense;
        public bool IsProfitable => Result > 0;
    }

    public async Task<IReadOnlyList<EventSummaryResponse>> ListAsync(
        Guid ownerId,
        EventFilter filter,
        CancellationToken cancellationToken = default)
    {
        var rows = await ProjectTotals(Filtered(ownerId, filter.From, filter.To, filter.Status)
                .OrderByDescending(e => e.EventDate)
                .ThenByDescending(e => e.CreatedAt))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new EventSummaryResponse(
            r.Id,
            r.Name,
            r.Description,
            r.EventDate,
            r.Status,
            r.Income,
            r.Expense,
            r.Result,
            r.IsProfitable,
            r.EntryCount))];
    }

    public async Task<EventDetailsResponse?> GetDetailsAsync(
        Guid eventId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var totals = await ProjectTotals(
                context.Events.AsNoTracking().Where(e => e.Id == eventId && e.OwnerId == ownerId))
            .FirstOrDefaultAsync(cancellationToken);

        if (totals is null) return null;

        var rows = await LedgerProjection
            .Project(
                context.Entries.AsNoTracking().Where(e => e.EventId == eventId && e.OwnerId == ownerId),
                context.Events.AsNoTracking())
            .OrderByDescending(e => e.OccurredOn)
            .ToListAsync(cancellationToken);

        var entries = rows.Select(LedgerProjection.ToResponse).ToList();

        return new EventDetailsResponse(
            totals.Id,
            totals.Name,
            totals.Description,
            totals.EventDate,
            totals.Status,
            totals.Income,
            totals.Expense,
            totals.Result,
            totals.IsProfitable,
            entries);
    }

    public async Task<CashReportResponse> GetCashReportAsync(
        Guid ownerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var rows = await ProjectTotals(
                Filtered(ownerId, from, to, status: null).OrderByDescending(e => e.EventDate))
            .ToListAsync(cancellationToken);

        var events = rows
            .Select(r => new EventResultResponse(
                r.Id,
                r.Name,
                r.EventDate,
                r.Income,
                r.Expense,
                r.Result,
                r.IsProfitable))
            .ToList();

        var totalIncome = rows.Sum(r => r.Income);
        var totalExpense = rows.Sum(r => r.Expense);

        return new CashReportResponse(
            totalIncome,
            totalExpense,
            totalIncome - totalExpense,
            rows.Count,
            rows.Count(r => r.Result > 0),
            rows.Count(r => r.Result < 0),
            rows.Count(r => r.Result == 0),
            events);
    }

    private IQueryable<Event> Filtered(Guid ownerId, DateOnly? from, DateOnly? to, EventStatus? status)
    {
        // O filtro por dono entra na consulta, não numa checagem posterior:
        // não há caminho de leitura que alcance o dado de outro usuário.
        var query = context.Events.AsNoTracking().Where(e => e.OwnerId == ownerId);

        if (from is { } start) query = query.Where(e => e.EventDate >= start);
        if (to is { } end) query = query.Where(e => e.EventDate <= end);
        if (status is { } wanted) query = query.Where(e => e.Status == wanted);

        return query;
    }

    /// <summary>
    /// O cast para <c>decimal?</c> evita que uma soma sem linhas volte nula do
    /// SQL Server. Lançamentos excluídos já ficam de fora pelo filtro global.
    /// </summary>
    private IQueryable<Totals> ProjectTotals(IQueryable<Event> query) =>
        query.Select(e => new Totals(
            e.Id,
            e.Name,
            e.Description,
            e.EventDate,
            e.Status,
            context.Entries
                .Where(x => x.EventId == e.Id && x.Type == EntryType.Income)
                .Sum(x => (decimal?)x.Amount) ?? 0m,
            context.Entries
                .Where(x => x.EventId == e.Id && x.Type == EntryType.Expense)
                .Sum(x => (decimal?)x.Amount) ?? 0m,
            context.Entries.Count(x => x.EventId == e.Id)));
}
