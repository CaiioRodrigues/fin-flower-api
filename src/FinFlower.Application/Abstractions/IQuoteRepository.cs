using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public interface IQuoteRepository
{
    /// <summary>Carrega o orçamento com os itens: sem eles o total não fecha.</summary>
    Task<Quote?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Quantos orçamentos o dono já emitiu no ano, para numerar o próximo.</summary>
    Task<int> CountInYearAsync(Guid ownerId, int year, CancellationToken cancellationToken = default);

    Task<bool> NumberExistsAsync(Guid ownerId, string number, CancellationToken cancellationToken = default);

    void Add(Quote quote);
}

public interface IQuoteQueries
{
    Task<IReadOnlyList<QuoteSummaryResponse>> ListAsync(
        Guid ownerId,
        QuoteFilter filter,
        DateOnly today,
        CancellationToken cancellationToken = default);

    Task<QuoteResponse?> GetAsync(
        Guid quoteId,
        Guid ownerId,
        DateOnly today,
        CancellationToken cancellationToken = default);
}
