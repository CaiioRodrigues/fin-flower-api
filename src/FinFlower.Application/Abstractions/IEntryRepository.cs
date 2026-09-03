using FinFlower.Domain.Entities;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Application.Abstractions;

/// <summary>
/// Lado de escrita do livro-caixa. Toda operação exige o <c>ownerId</c> — não
/// existe forma de carregar um lançamento sem dizer de quem ele é.
/// </summary>
public interface IEntryRepository
{
    Task<Entry?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>O lançamento que uma parcela criou, para o estorno removê-lo.</summary>
    Task<Entry?> GetByInstallmentAsync(Guid installmentId, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// As competências de um item fixo que já viraram lançamento. É o que torna
    /// a geração do mês idempotente: gerar duas vezes não duplica a despesa.
    /// </summary>
    Task<IReadOnlySet<(Guid RecurringItemId, DateOnly Month)>> GetGeneratedRecurringMonthsAsync(
        Guid ownerId,
        YearMonth competence,
        CancellationToken cancellationToken = default);

    /// <summary>Os lançamentos ligados a um evento, para excluí-lo em cascata.</summary>
    Task<IReadOnlyList<Entry>> ListByEventAsync(Guid eventId, Guid ownerId, CancellationToken cancellationToken = default);

    void Add(Entry entry);
    void AddRange(IEnumerable<Entry> entries);
}
