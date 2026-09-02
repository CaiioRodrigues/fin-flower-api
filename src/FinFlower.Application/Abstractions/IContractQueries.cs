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

    /// <summary>Parcelas em aberto agrupadas por mês de vencimento, mais as vencidas.</summary>
    Task<CashFlowReportResponse> GetCashFlowAsync(
        Guid ownerId,
        DateOnly today,
        int monthsAhead,
        CancellationToken cancellationToken = default);
}
