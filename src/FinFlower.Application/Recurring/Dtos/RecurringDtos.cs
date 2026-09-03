using FinFlower.Domain.Enums;

namespace FinFlower.Application.Recurring.Dtos;

public sealed record CreateRecurringItemRequest(
    RecurringKind Kind,
    string Description,
    decimal Amount,
    string Category,
    int DayOfMonth,
    string StartMonth,
    string? EndMonth,
    string? Notes) : Validators.IRecurringItemFields;

public sealed record UpdateRecurringItemRequest(
    string Description,
    decimal Amount,
    string Category,
    int DayOfMonth,
    string? EndMonth,
    string? Notes) : Validators.IRecurringItemFields;

public sealed record RecurringItemResponse(
    Guid Id,
    RecurringKind Kind,
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    int DayOfMonth,
    string StartMonth,
    string? EndMonth,
    bool IsActive,
    string? Notes,
    // Se a competência consultada já foi lançada no caixa.
    bool GeneratedForMonth,
    // Se o item vale para a competência consultada.
    bool DueInMonth,
    DateOnly? DueDate);

/// <summary>
/// A previsão do mês: quanto de fixo está contratado, quanto já virou lançamento
/// e quanto falta gerar. É o que a tela usa para oferecer "lançar o mês".
/// </summary>
public sealed record RecurringMonthResponse(
    string Competence,
    string Label,
    decimal TotalFixedExpense,
    decimal TotalProLabore,
    decimal TotalFixedIncome,
    decimal PendingAmount,
    int PendingCount,
    IReadOnlyList<RecurringItemResponse> Items);

/// <summary>O que a geração de um mês efetivamente fez.</summary>
public sealed record GenerateMonthResponse(
    string Competence,
    int Generated,
    int AlreadyExisted,
    decimal GeneratedAmount,
    IReadOnlyList<string> Descriptions);

public sealed record RecurringFilter(RecurringKind? Kind = null, bool? OnlyActive = null);
