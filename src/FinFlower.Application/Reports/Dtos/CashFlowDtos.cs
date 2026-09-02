using FinFlower.Domain.Enums;

namespace FinFlower.Application.Reports.Dtos;

/// <summary>Uma parcela em aberto, na visão do fluxo de caixa.</summary>
public sealed record ScheduledInstallmentResponse(
    Guid ContractId,
    Guid EventId,
    string EventName,
    string Counterparty,
    ContractDirection Direction,
    PaymentMethod PaymentMethod,
    int Number,
    decimal Amount,
    DateOnly DueDate,
    bool IsOverdue);

/// <summary>Total previsto de um mês, separado por sentido.</summary>
public sealed record MonthlyForecastResponse(
    int Year,
    int Month,
    decimal Receivable,
    decimal Payable,
    decimal Net,
    int InstallmentCount);

public sealed record OverdueSummaryResponse(
    decimal Receivable,
    decimal Payable,
    int InstallmentCount);

/// <summary>
/// Fluxo de caixa: o que já entrou e saiu, o que está vencido, o que vence neste
/// mês e a previsão dos próximos.
/// </summary>
public sealed record CashFlowReportResponse(
    DateOnly ReferenceDate,

    // Saldo já realizado: soma dos lançamentos de todos os eventos.
    decimal RealizedBalance,

    OverdueSummaryResponse Overdue,
    MonthlyForecastResponse CurrentMonth,
    IReadOnlyList<MonthlyForecastResponse> UpcomingMonths,

    // Tudo que ainda está em aberto, sem recorte de período.
    decimal TotalReceivable,
    decimal TotalPayable,

    // Realizado somado a tudo que está previsto.
    decimal ProjectedBalance,

    IReadOnlyList<ScheduledInstallmentResponse> Overdues,
    IReadOnlyList<ScheduledInstallmentResponse> NextDue);
