using FinFlower.Application.Abstractions;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class QuoteRepository(AppDbContext context) : IQuoteRepository
{
    public Task<Quote?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        context.Quotes
            // Sem os itens o total do orçamento seria zero, e aprovar geraria
            // um contrato vazio.
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId, cancellationToken);

    public Task<int> CountInYearAsync(Guid ownerId, int year, CancellationToken cancellationToken = default) =>
        context.Quotes
            .AsNoTracking()
            .CountAsync(q => q.OwnerId == ownerId && q.IssuedOn.Year == year, cancellationToken);

    public Task<bool> NumberExistsAsync(Guid ownerId, string number, CancellationToken cancellationToken = default) =>
        context.Quotes
            .AsNoTracking()
            .AnyAsync(q => q.OwnerId == ownerId && q.Number == number, cancellationToken);

    public void Add(Quote quote) => context.Quotes.Add(quote);
}
