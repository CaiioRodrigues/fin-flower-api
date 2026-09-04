using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Application.Abstractions;

/// <summary>
/// Lado de leitura do caixa. Soma no banco, não em memória: o mês a mês de dois
/// anos é uma consulta agrupada, não vinte mil linhas trazidas para cá.
/// </summary>
public interface IEntryQueries
{
    Task<LedgerPageResponse> ListAsync(
        Guid ownerId,
        EntryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<LedgerEntryResponse?> GetAsync(Guid entryId, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Os totais por mês, sentido, categoria e origem do intervalo pedido.
    /// <paramref name="since"/> é a data do saldo inicial, quando existe: o que
    /// vem antes dela já está contido nele e ficaria contado duas vezes.
    /// </summary>
    Task<IReadOnlyList<MonthlyBucket>> GetMonthlyBucketsAsync(
        Guid ownerId,
        YearMonth from,
        YearMonth to,
        DateOnly? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saldo acumulado antes da competência: entradas menos saídas de tudo que
    /// veio antes. É o que faz o primeiro mês da tela não começar do zero.
    /// </summary>
    Task<decimal> GetBalanceBeforeAsync(
        Guid ownerId,
        YearMonth competence,
        DateOnly? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quantos lançamentos são anteriores à data do saldo inicial. Não entram na
    /// conta — mas o número precisa aparecer na tela: um lançamento que some do
    /// saldo sem explicação parece defeito, e com a explicação é regra.
    /// </summary>
    Task<int> CountBeforeAsync(Guid ownerId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// As competências de itens fixos já lançadas no intervalo. É o que permite
    /// prever só o que falta gerar, sem contar duas vezes o mês já lançado.
    /// </summary>
    Task<IReadOnlySet<(Guid RecurringItemId, DateOnly Month)>> GetGeneratedRecurringMonthsAsync(
        Guid ownerId,
        YearMonth from,
        YearMonth to,
        CancellationToken cancellationToken = default);

    /// <summary>As categorias já usadas pelo dono, para o formulário sugerir.</summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(Guid ownerId, CancellationToken cancellationToken = default);
}
