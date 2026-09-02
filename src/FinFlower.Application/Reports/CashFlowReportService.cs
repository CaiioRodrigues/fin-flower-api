using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Reports.Dtos;

namespace FinFlower.Application.Reports;

public interface ICashFlowReportService
{
    Task<Result<CashFlowReportResponse>> GetAsync(int monthsAhead, CancellationToken ct = default);
}

/// <summary>
/// Fluxo de caixa: junta o realizado (lançamentos) com o previsto (parcelas em
/// aberto) para responder quanto entra neste mês e nos próximos.
/// </summary>
public sealed class CashFlowReportService(
    IContractQueries queries,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ICashFlowReportService
{
    private const int DefaultMonthsAhead = 6;
    private const int MaxMonthsAhead = 24;

    public async Task<Result<CashFlowReportResponse>> GetAsync(int monthsAhead, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<CashFlowReportResponse>(
                Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente."));
        }

        if (monthsAhead is < 0 or > MaxMonthsAhead)
        {
            return Result.Failure<CashFlowReportResponse>(Error.Validation(
                "report.invalid_horizon",
                $"A previsão deve cobrir de 0 a {MaxMonthsAhead} meses."));
        }

        var horizon = monthsAhead == 0 ? DefaultMonthsAhead : monthsAhead;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        return Result.Success(await queries.GetCashFlowAsync(ownerId, today, horizon, ct));
    }
}
