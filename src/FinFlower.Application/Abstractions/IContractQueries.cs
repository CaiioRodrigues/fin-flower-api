using FinFlower.Application.Contracts;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Reports.Dtos;

namespace FinFlower.Application.Abstractions;

/// <summary>
/// Lado de leitura dos contratos. Projeta para DTO no banco, então a listagem e
/// os relatórios somam em SQL — e o conteúdo do PDF nunca é carregado só para
/// mostrar o nome do arquivo.
/// </summary>
public interface IContractQueries
{
    Task<IReadOnlyList<ContractSummaryResponse>> ListAsync(
        Guid ownerId,
        ContractFilter filter,
        DateOnly today,
        CancellationToken cancellationToken = default);

    Task<ContractResponse?> GetAsync(
        Guid contractId,
        Guid ownerId,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// O previsto que vem dos contratos: parcelas em aberto que vencem dentro do
    /// intervalo, agrupadas por mês e sentido. O vencido sai por
    /// <see cref="GetOverdueTotalsAsync"/>, porque não pertence a mês futuro nenhum.
    /// </summary>
    Task<IReadOnlyList<Cash.Dtos.InstallmentForecastBucket>> GetInstallmentForecastAsync(
        Guid ownerId,
        Domain.ValueObjects.YearMonth from,
        Domain.ValueObjects.YearMonth to,
        DateOnly today,
        CancellationToken cancellationToken = default);

    Task<Cash.Dtos.OverdueTotals> GetOverdueTotalsAsync(
        Guid ownerId,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>Parcelas em aberto agrupadas por mês de vencimento, mais as vencidas.</summary>
    Task<CashFlowReportResponse> GetCashFlowAsync(
        Guid ownerId,
        DateOnly today,
        int monthsAhead,
        CancellationToken cancellationToken = default);
}
