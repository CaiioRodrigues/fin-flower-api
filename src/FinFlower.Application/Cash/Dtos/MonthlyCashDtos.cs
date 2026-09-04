using FinFlower.Domain.Enums;

namespace FinFlower.Application.Cash.Dtos;

/// <summary>Um total por categoria dentro do mês, para a tela mostrar onde o dinheiro foi.</summary>
public sealed record CategoryTotal(string Category, decimal Amount, int Count);

/// <summary>
/// Um mês do caixa. <c>OpeningBalance</c> é o fechamento do mês anterior, então
/// a sequência lida de cima a baixo conta a história inteira do dinheiro.
///
/// Os campos vêm em dois pares. <c>Income</c>/<c>Expense</c> são o realizado —
/// dinheiro que se moveu. <c>Expected*</c> é o previsto do mês: parcelas em
/// aberto que vencem nele mais itens fixos que ainda não viraram lançamento.
/// Mês passado não tem previsto: o que aconteceu, aconteceu.
/// </summary>
public sealed record MonthlyCashMonth(
    string Competence,
    int Year,
    int Month,
    string Label,
    decimal OpeningBalance,
    decimal Income,
    decimal Expense,
    decimal Result,
    decimal ClosingBalance,
    bool IsForecast,
    decimal ExpectedIncome,
    decimal ExpectedExpense,
    decimal ProjectedResult,
    decimal ProjectedBalance,
    decimal FixedExpense,
    decimal ProLabore,
    decimal EventIncome,
    decimal EventExpense,
    decimal ContractIncome,
    decimal ContractExpense,
    int EntryCount,
    IReadOnlyList<CategoryTotal> IncomeByCategory,
    IReadOnlyList<CategoryTotal> ExpenseByCategory);

/// <summary>
/// O caixa completo de um intervalo de competências. Traz o saldo que vinha de
/// antes, para o primeiro mês do intervalo não começar do zero e a projeção
/// fazer sentido.
/// </summary>
public sealed record MonthlyCashResponse(
    string From,
    string To,
    decimal OpeningBalance,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Result,
    decimal ClosingBalance,
    decimal AverageMonthlyResult,
    decimal TotalFixedExpense,
    decimal TotalProLabore,

    // Saldo se tudo que está previsto acontecer. Não inclui o vencido, que é
    // reportado à parte por não pertencer a mês nenhum do futuro.
    decimal ProjectedBalance,
    decimal TotalExpectedIncome,
    decimal TotalExpectedExpense,
    decimal OverdueReceivable,
    decimal OverduePayable,

    int BestMonthIndex,
    int WorstMonthIndex,

    // O saldo inicial declarado pelo dono, quando existe. Sem ele o "saldo em
    // caixa" é só a soma do que foi digitado, e quem começou a usar o sistema no
    // meio do ano lê variação achando que lê saldo.
    CashOpeningResponse? Opening,

    IReadOnlyList<MonthlyCashMonth> Months);

/// <summary>
/// O dinheiro que já existia quando o sistema começou a ser usado.
/// <c>IgnoredEntries</c> conta os lançamentos anteriores à data: eles ficam de
/// fora do saldo porque este valor já os contém, e a tela precisa dizer isso.
/// </summary>
public sealed record CashOpeningResponse(
    decimal Amount,
    DateOnly OccurredOn,
    string? Notes,
    int IgnoredEntries);

/// <summary>
/// O saldo declarado. <c>Amount</c> aceita negativo: começar devendo é uma
/// situação real, e recusá-la obrigaria a mentir para o próprio caixa.
/// </summary>
public sealed record SaveCashOpeningRequest(
    decimal Amount,
    DateOnly OccurredOn,
    string? Notes);

/// <summary>
/// Parcelas em aberto agrupadas por mês de vencimento e sentido. É o previsto
/// que vem dos contratos; o que vem dos itens fixos é calculado a partir da
/// vigência deles, sem passar pelo banco.
/// </summary>
public sealed record InstallmentForecastBucket(
    int Year,
    int Month,
    Domain.Enums.ContractDirection Direction,
    decimal Amount,
    int Count);

/// <summary>O que venceu e não foi liquidado, separado por sentido.</summary>
public sealed record OverdueTotals(decimal Receivable, decimal Payable);

/// <summary>
/// Linha crua do agrupamento no banco: um total por mês, sentido, categoria e
/// origem. A composição do relatório acontece em memória a partir disto, com
/// uma ida ao banco só.
/// </summary>
public sealed record MonthlyBucket(
    int Year,
    int Month,
    EntryType Type,
    string Category,
    EntrySource Source,
    RecurringKind? RecurringKind,
    bool HasEvent,
    decimal Amount,
    int Count);
