namespace FinFlower.Domain.Enums;

/// <summary>
/// Natureza do item fixo. Define o sentido do lançamento gerado e separa as
/// telas — gasto fixo e pró-labore são a mesma mecânica, mas o dono quer olhar
/// para eles em lugares diferentes, e o pró-labore não é "custo do negócio".
/// </summary>
public enum RecurringKind
{
    /// <summary>Despesa que se repete todo mês: aluguel, internet, contador.</summary>
    FixedExpense = 1,

    /// <summary>Retirada mensal do sócio.</summary>
    ProLabore = 2,

    /// <summary>Receita que se repete todo mês: contrato de manutenção, assinatura.</summary>
    FixedIncome = 3,
}
