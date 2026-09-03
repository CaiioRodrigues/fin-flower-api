using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Infrastructure.Persistence.Queries;

/// <summary>
/// O lançamento como o banco o devolve, antes de virar resposta. A competência
/// e o sinal são montados aqui, em memória: são strings e contas triviais sobre
/// uma página já reduzida, e empurrá-las para o SQL só produziria CONVERTs
/// difíceis de ler no plano de execução.
/// </summary>
internal sealed record LedgerRow(
    Guid Id,
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    DateOnly OccurredOn,
    EntrySource Source,
    Guid? EventId,
    string? EventName);

internal static class LedgerProjection
{
    /// <summary>
    /// A projeção usada por toda leitura de lançamento. O nome do evento vem por
    /// junção à esquerda — lançamento sem evento é a regra, não a exceção.
    /// </summary>
    public static IQueryable<LedgerRow> Project(IQueryable<Entry> entries, IQueryable<Event> events) =>
        entries.Select(e => new LedgerRow(
            e.Id,
            e.Type,
            e.Description,
            e.Amount,
            e.Category,
            e.OccurredOn,
            e.Source,
            e.EventId,
            events.Where(v => v.Id == e.EventId).Select(v => v.Name).FirstOrDefault()));

    public static LedgerEntryResponse ToResponse(this LedgerRow row) => new(
        row.Id,
        row.Type,
        row.Description,
        row.Amount,
        row.Type == EntryType.Income ? row.Amount : -row.Amount,
        row.Category,
        row.OccurredOn,
        YearMonth.From(row.OccurredOn).ToString(),
        row.Source,
        row.EventId,
        row.EventName,
        // O que veio de contrato pertence à parcela; o resto a tela pode editar.
        row.Source != EntrySource.Contract);
}
