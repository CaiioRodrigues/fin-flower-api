using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Reports.Dtos;

namespace FinFlower.Application.Reports;

public interface ICashReportService
{
    Task<Result<CashReportResponse>> GetAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

/// <summary>
/// Caixa consolidado do usuário: soma dos resultados de todos os eventos no
/// período, com a contagem de quantos deram lucro e quantos deram prejuízo.
/// </summary>
public sealed class CashReportService(IEventQueries queries, ICurrentUser currentUser) : ICashReportService
{
    public async Task<Result<CashReportResponse>> GetAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<CashReportResponse>(
                Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente."));
        }

        if (from is not null && to is not null && from > to)
        {
            return Result.Failure<CashReportResponse>(
                Error.Validation("report.invalid_period", "A data inicial não pode ser maior que a final."));
        }

        return Result.Success(await queries.GetCashReportAsync(ownerId, from, to, ct));
    }
}
