using FinFlower.Domain.Enums;

namespace FinFlower.Application.Cash.Dtos;

/// <summary>Um total por categoria dentro do mês, para a tela mostrar onde o dinheiro foi.</summary>
public sealed record CategoryTotal(string Category, decimal Amount, int Count);

/// <summary>
/// O mês fechado: o que entrou, o que saiu, o que sobrou, e o saldo acumulado
/// depois dele. <c>OpeningBalance</c> é o fechamento do mês anterior, então a
/// sequência de meses lida de cima a baixo conta a história inteira do caixa.
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
    int BestMonthIndex,
    int WorstMonthIndex,
    IReadOnlyList<MonthlyCashMonth> Months);

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
