namespace FinFlower.Application.Reports.Dtos;

/// <summary>Linha do caixa: o resultado de um evento.</summary>
public sealed record EventResultResponse(
    Guid EventId,
    string Name,
    DateOnly EventDate,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Result,
    bool IsProfitable);

/// <summary>
/// Caixa consolidado: quanto entrou, quanto saiu, o saldo e quantos eventos
/// deram lucro contra quantos deram prejuízo.
/// </summary>
public sealed record CashReportResponse(
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Balance,
    int EventCount,
    int ProfitableEventCount,
    int UnprofitableEventCount,
    int BreakEvenEventCount,
    IReadOnlyList<EventResultResponse> Events);
