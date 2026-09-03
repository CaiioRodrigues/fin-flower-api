using FinFlower.Application.Abstractions;
using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Common;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Application.Cash;

public interface IMonthlyCashService
{
    Task<Result<MonthlyCashResponse>> GetAsync(string? from, string? to, CancellationToken ct = default);
}

/// <summary>
/// O caixa completo, mês a mês. Uma consulta agrupada traz os totais e a
/// composição acontece aqui: o saldo de cada mês é o fechamento do anterior,
/// então a leitura de cima a baixo conta a história do dinheiro sem que a tela
/// precise somar nada.
/// </summary>
public sealed class MonthlyCashService(
    IEntryQueries queries,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IMonthlyCashService
{
    /// <summary>Doze meses é a janela que a tela abre por padrão.</summary>
    public const int DefaultMonths = 12;

    /// <summary>Teto do intervalo: cinco anos de uma vez já é relatório, não tela.</summary>
    public const int MaxMonths = 60;

    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    public async Task<Result<MonthlyCashResponse>> GetAsync(
        string? from,
        string? to,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<MonthlyCashResponse>(NoSession);

        var range = ResolveRange(from, to);
        if (range.IsFailure) return Result.Failure<MonthlyCashResponse>(range.Error!);

        var (start, end) = range.Value;

        var opening = await queries.GetBalanceBeforeAsync(ownerId, start, ct);
        var buckets = await queries.GetMonthlyBucketsAsync(ownerId, start, end, ct);

        return Result.Success(Compose(start, end, opening, buckets));
    }

    /// <summary>
    /// Resolve o intervalo pedido. Em branco, os doze meses que terminam no mês
    /// corrente — o recorte com que quem opera o caixa olha para ele.
    /// </summary>
    internal Result<(YearMonth From, YearMonth To)> ResolveRange(string? from, string? to)
    {
        var current = YearMonth.From(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        if (!TryRead(from, out var start)) return Invalid("de");
        if (!TryRead(to, out var end)) return Invalid("até");

        // Só um dos lados informado ancora a janela padrão nele, em vez de
        // devolver um intervalo vazio ou o ano inteiro.
        (start, end) = (start, end) switch
        {
            (null, null) => (current.AddMonths(-(DefaultMonths - 1)), current),
            ({ } s, null) => (s, s.AddMonths(DefaultMonths - 1)),
            (null, { } e) => (e.AddMonths(-(DefaultMonths - 1)), e),
            ({ } s, { } e) => (s, e),
        };

        if (start > end)
            (start, end) = (end, start);

        var months = start!.Value.MonthsUntil(end!.Value) + 1;
        if (months > MaxMonths)
        {
            return Result.Failure<(YearMonth, YearMonth)>(Error.Validation(
                "cash.range_too_wide",
                $"O intervalo não pode passar de {MaxMonths} meses."));
        }

        return Result.Success((start.Value, end.Value));

        static bool TryRead(string? value, out YearMonth? competence)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                competence = null;
                return true;
            }

            if (YearMonth.TryParse(value, out var parsed))
            {
                competence = parsed;
                return true;
            }

            competence = null;
            return false;
        }

        static Result<(YearMonth, YearMonth)> Invalid(string field) =>
            Result.Failure<(YearMonth, YearMonth)>(Error.Validation(
                "cash.invalid_competence",
                $"O campo '{field}' deve estar no formato aaaa-mm, como 2026-09."));
    }

    internal static MonthlyCashResponse Compose(
        YearMonth start,
        YearMonth end,
        decimal opening,
        IReadOnlyList<MonthlyBucket> buckets)
    {
        var byMonth = buckets
            .GroupBy(b => new YearMonth(b.Year, b.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        var months = new List<MonthlyCashMonth>();
        var running = opening;

        foreach (var competence in YearMonth.Range(start, end))
        {
            // Mês sem lançamento nenhum continua na lista: um buraco no meio da
            // série esconderia justamente o mês em que nada entrou.
            var rows = byMonth.TryGetValue(competence, out var found) ? found : [];

            var income = Sum(rows, EntryType.Income);
            var expense = Sum(rows, EntryType.Expense);
            var monthOpening = running;
            running += income - expense;

            months.Add(new MonthlyCashMonth(
                competence.ToString(),
                competence.Year,
                competence.Month,
                MonthLabel.For(competence),
                monthOpening,
                income,
                expense,
                income - expense,
                running,
                FixedExpense: rows
                    .Where(b => b.RecurringKind == RecurringKind.FixedExpense)
                    .Sum(b => b.Amount),
                ProLabore: rows
                    .Where(b => b.RecurringKind == RecurringKind.ProLabore)
                    .Sum(b => b.Amount),
                EventIncome: rows
                    .Where(b => b.HasEvent && b.Type == EntryType.Income)
                    .Sum(b => b.Amount),
                EventExpense: rows
                    .Where(b => b.HasEvent && b.Type == EntryType.Expense)
                    .Sum(b => b.Amount),
                ContractIncome: rows
                    .Where(b => b.Source == EntrySource.Contract && b.Type == EntryType.Income)
                    .Sum(b => b.Amount),
                ContractExpense: rows
                    .Where(b => b.Source == EntrySource.Contract && b.Type == EntryType.Expense)
                    .Sum(b => b.Amount),
                EntryCount: rows.Sum(b => b.Count),
                IncomeByCategory: ByCategory(rows, EntryType.Income),
                ExpenseByCategory: ByCategory(rows, EntryType.Expense)));
        }

        var totalIncome = months.Sum(m => m.Income);
        var totalExpense = months.Sum(m => m.Expense);

        return new MonthlyCashResponse(
            start.ToString(),
            end.ToString(),
            opening,
            totalIncome,
            totalExpense,
            totalIncome - totalExpense,
            running,
            AverageMonthlyResult: months.Count == 0
                ? 0m
                : decimal.Round((totalIncome - totalExpense) / months.Count, 2, MidpointRounding.AwayFromZero),
            TotalFixedExpense: months.Sum(m => m.FixedExpense),
            TotalProLabore: months.Sum(m => m.ProLabore),
            BestMonthIndex: IndexOfExtreme(months, best: true),
            WorstMonthIndex: IndexOfExtreme(months, best: false),
            Months: months);
    }

    private static decimal Sum(List<MonthlyBucket> rows, EntryType type) =>
        rows.Where(b => b.Type == type).Sum(b => b.Amount);

    private static IReadOnlyList<CategoryTotal> ByCategory(List<MonthlyBucket> rows, EntryType type) =>
    [
        .. rows
            .Where(b => b.Type == type)
            .GroupBy(b => b.Category)
            .Select(g => new CategoryTotal(g.Key, g.Sum(b => b.Amount), g.Sum(b => b.Count)))
            .OrderByDescending(c => c.Amount)
            .ThenBy(c => c.Category, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// O melhor e o pior mês do intervalo, por posição. Meses sem lançamento
    /// ficam de fora: um mês vazio não é o pior resultado, é ausência de dado.
    /// </summary>
    private static int IndexOfExtreme(List<MonthlyCashMonth> months, bool best)
    {
        var index = -1;

        for (var i = 0; i < months.Count; i++)
        {
            if (months[i].EntryCount == 0) continue;

            if (index < 0
                || (best && months[i].Result > months[index].Result)
                || (!best && months[i].Result < months[index].Result))
            {
                index = i;
            }
        }

        return index;
    }
}
