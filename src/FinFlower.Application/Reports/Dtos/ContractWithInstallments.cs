using FinFlower.Application.Contracts.Dtos;

namespace FinFlower.Application.Reports.Dtos;

/// <summary>Contrato com as parcelas, como o extrato do evento precisa.</summary>
public sealed record ContractWithInstallments(ContractResponse Contract);
