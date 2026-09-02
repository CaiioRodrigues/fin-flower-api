namespace FinFlower.Domain.Enums;

/// <summary>Sentido do contrato: dinheiro que entra ou que sai.</summary>
public enum ContractDirection
{
    /// <summary>A receber — contrato com cliente.</summary>
    Receivable = 1,

    /// <summary>A pagar — contrato com fornecedor.</summary>
    Payable = 2,
}
