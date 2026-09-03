using FinFlower.Domain.Enums;

namespace FinFlower.Application.Entries.Dtos;

public sealed record CreateEntryRequest(
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    DateOnly OccurredOn,
    Guid? EventId = null) : Validators.IEntryFields;

public sealed record UpdateEntryRequest(
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    DateOnly OccurredOn,
    Guid? EventId = null) : Validators.IEntryFields;

/// <summary>
/// Um lançamento como o livro-caixa o mostra: já com o nome do evento e a origem,
/// para a tela não precisar de uma segunda consulta só para escrever "Casamento X".
/// </summary>
public sealed record LedgerEntryResponse(
    Guid Id,
    EntryType Type,
    string Description,
    decimal Amount,
    decimal SignedAmount,
    string Category,
    DateOnly OccurredOn,
    string Competence,
    EntrySource Source,
    Guid? EventId,
    string? EventName,
    bool IsEditable);

/// <summary>Filtros do livro-caixa. Todos opcionais e combináveis.</summary>
public sealed record EntryFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    EntryType? Type = null,
    EntrySource? Source = null,
    Guid? EventId = null,
    // true traz só o que não tem evento; false, só o que tem.
    bool? WithoutEvent = null,
    string? Category = null,
    string? Search = null);

/// <summary>Uma página do livro-caixa com os totais do filtro inteiro, não só da página.</summary>
public sealed record LedgerPageResponse(
    IReadOnlyList<LedgerEntryResponse> Entries,
    int TotalCount,
    int Page,
    int PageSize,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Result);
