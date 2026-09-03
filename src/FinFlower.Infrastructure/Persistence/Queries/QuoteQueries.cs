using FinFlower.Application.Abstractions;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Queries;

public sealed class QuoteQueries(AppDbContext context) : IQuoteQueries
{
    public async Task<IReadOnlyList<QuoteSummaryResponse>> ListAsync(
        Guid ownerId,
        QuoteFilter filter,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var query = context.Quotes.AsNoTracking().Where(q => q.OwnerId == ownerId);

        if (filter.Status is { } status) query = query.Where(q => q.Status == status);
        if (filter.EventId is { } eventId) query = query.Where(q => q.EventId == eventId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(q =>
                q.Number.Contains(term) || q.ClientName.Contains(term) || q.Title.Contains(term));
        }

        var rows = await query
            .OrderByDescending(q => q.IssuedOn)
            .ThenByDescending(q => q.CreatedAt)
            .Select(q => new
            {
                q.Id,
                q.Number,
                q.ClientName,
                q.Title,
                q.IssuedOn,
                q.ValidUntil,
                q.Status,
                q.DiscountAmount,
                q.EventId,
                q.ContractId,
                EventName = context.Events.Where(e => e.Id == q.EventId).Select(e => e.Name).FirstOrDefault(),
                // O subtotal sai da soma das linhas no banco: trazer os itens
                // de cada orçamento só para somá-los seria N+1 na listagem.
                Subtotal = context.QuoteItems
                    .Where(i => i.QuoteId == q.Id)
                    .Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0m,
                ItemCount = context.QuoteItems.Count(i => i.QuoteId == q.Id),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r =>
            {
                var subtotal = decimal.Round(r.Subtotal, 2, MidpointRounding.AwayFromZero);

                return new QuoteSummaryResponse(
                    r.Id,
                    r.Number,
                    r.ClientName,
                    r.Title,
                    r.IssuedOn,
                    r.ValidUntil,
                    r.Status,
                    IsExpired(r.Status, r.ValidUntil, today),
                    subtotal,
                    r.DiscountAmount,
                    Math.Max(0m, subtotal - r.DiscountAmount),
                    r.ItemCount,
                    r.EventId,
                    r.EventName,
                    r.ContractId);
            }),
        ];
    }

    public async Task<QuoteResponse?> GetAsync(
        Guid quoteId,
        Guid ownerId,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Quotes
            .AsNoTracking()
            .Where(q => q.Id == quoteId && q.OwnerId == ownerId)
            .Select(q => new
            {
                q.Id,
                q.Number,
                q.ClientName,
                q.Title,
                q.IssuedOn,
                q.ValidUntil,
                q.Status,
                q.Notes,
                q.DiscountAmount,
                q.EventId,
                q.ContractId,
                EventName = context.Events.Where(e => e.Id == q.EventId).Select(e => e.Name).FirstOrDefault(),
                Items = context.QuoteItems
                    .Where(i => i.QuoteId == q.Id)
                    .OrderBy(i => i.Position)
                    .Select(i => new
                    {
                        i.Id,
                        i.Position,
                        i.Description,
                        i.Quantity,
                        i.UnitPrice,
                        i.Unit,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        // O total de cada linha é arredondado antes de somar, como o cliente lê
        // a proposta: somar primeiro e arredondar depois daria um centavo a mais.
        var items = row.Items
            .Select(i => new QuoteItemResponse(
                i.Id,
                i.Position,
                i.Description,
                i.Quantity,
                i.UnitPrice,
                i.Unit,
                decimal.Round(i.Quantity * i.UnitPrice, 2, MidpointRounding.AwayFromZero)))
            .ToList();

        var subtotal = items.Sum(i => i.Total);

        return new QuoteResponse(
            row.Id,
            row.Number,
            row.ClientName,
            row.Title,
            row.IssuedOn,
            row.ValidUntil,
            row.Status,
            IsExpired(row.Status, row.ValidUntil, today),
            row.Status is QuoteStatus.Draft or QuoteStatus.Sent,
            row.Notes,
            subtotal,
            row.DiscountAmount,
            Math.Max(0m, subtotal - row.DiscountAmount),
            row.EventId,
            row.EventName,
            row.ContractId,
            items);
    }

    private static bool IsExpired(QuoteStatus status, DateOnly validUntil, DateOnly today) =>
        status is QuoteStatus.Draft or QuoteStatus.Sent && validUntil < today;
}
