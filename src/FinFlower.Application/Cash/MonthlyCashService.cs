using FinFlower.Application.Abstractions;
using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Common;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Application.Cash;

public interface IMonthlyCashService
{
    Task<Result<MonthlyCashResponse>> GetAsync(string? from, string? to, CancellationToken ct = default);
}

/// <summary>
/// O caixa completo numa linha do tempo só: meses passados pelo que de fato se
/// moveu, meses futuros pelo que está previsto.
///
/// O previsto tem duas fontes, e ignorar qualquer uma delas dá um número
/// otimista: as parcelas de contrato em aberto (o que entra) e os itens fixos
/// que ainda não viraram lançamento (o aluguel e o pró-labore que vão sair de
/// qualquer jeito). Contar só as parcelas mostraria dinheiro entrando sem o
/// custo que vem junto.
/// </summary>
public sealed class MonthlyCashService(
    IEntryQueries queries,
    IContractQueries contracts,
    IRecurringItemRepository recurringItems,
    ICashOpeningRepository openings,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IMonthlyCashService
{
    /// <summary>Doze meses é a janela que a tela abre por padrão.</summary>
    public const int DefaultMonths = 12;

    /// <summary>
    /// Quantos dos doze ficam no futuro. A janela é centrada no mês corrente
    /// porque um caixa serve tanto para ver de onde se veio quanto para saber
    /// se dá para pagar as contas do trimestre.
    /// </summary>
    public const int DefaultMonthsAhead = 6;

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

        var today = Today;
        var current = YearMonth.From(today);

        // O saldo declarado pelo dono é o piso de tudo: define de quando a
        // história registrada vale e entra no acumulado no mês a que se refere.
        var declared = await openings.GetAsync(ownerId, ct);
        var since = declared?.OccurredOn;

        var opening = await queries.GetBalanceBeforeAsync(ownerId, start, since, ct);
        if (declared is { } d && d.OccurredOn < start.FirstDay) opening += d.Amount;

        var buckets = await queries.GetMonthlyBucketsAsync(ownerId, start, end, since, ct);

        var openingResponse = declared is null
            ? null
            : new CashOpeningResponse(
                declared.Amount,
                declared.OccurredOn,
                declared.Notes,
                await queries.CountBeforeAsync(ownerId, declared.OccurredOn, ct));

        // O previsto só é buscado quando a janela alcança o futuro: olhar um ano
        // fechado para trás não precisa de nada disso.
        var forecast = end >= current
            ? await contracts.GetInstallmentForecastAsync(ownerId, start, end, today, ct)
            : [];

        var overdue = end >= current
            ? await contracts.GetOverdueTotalsAsync(ownerId, today, ct)
            : new OverdueTotals(0m, 0m);

        var recurring = end >= current
            ? await recurringItems.ListAsync(ownerId, new RecurringFilter(OnlyActive: true), ct)
            : [];

        var generated = recurring.Count > 0
            ? await queries.GetGeneratedRecurringMonthsAsync(ownerId, start, end, ct)
            : new HashSet<(Guid, DateOnly)>();

        return Result.Success(Compose(
            start, end, current, opening, buckets, forecast, overdue, recurring, generated,
            openingResponse, declared?.Competence));
    }

    /// <summary>
    /// Resolve o intervalo pedido. Em branco, a janela é centrada no mês
    /// corrente: seis meses para trás e seis para a frente.
    /// </summary>
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    internal Result<(YearMonth From, YearMonth To)> ResolveRange(string? from, string? to)
    {
        var current = YearMonth.From(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        if (!TryRead(from, out var start)) return Invalid("de");
        if (!TryRead(to, out var end)) return Invalid("até");

        // Só um dos lados informado ancora a janela padrão nele, em vez de
        // devolver um intervalo vazio ou o ano inteiro.
        (start, end) = (start, end) switch
        {
            (null, null) => (
                current.AddMonths(-(DefaultMonths - DefaultMonthsAhead - 1)),
                current.AddMonths(DefaultMonthsAhead)),
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
        YearMonth current,
        decimal opening,
        IReadOnlyList<MonthlyBucket> buckets,
        IReadOnlyList<InstallmentForecastBucket> forecast,
        OverdueTotals overdue,
        IReadOnlyList<RecurringItem> recurring,
        IReadOnlySet<(Guid RecurringItemId, DateOnly Month)> generated,
        CashOpeningResponse? declared = null,
        YearMonth? declaredIn = null)
    {
        var byMonth = buckets
            .GroupBy(b => new YearMonth(b.Year, b.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        var forecastByMonth = forecast
            .GroupBy(f => new YearMonth(f.Year, f.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        var months = new List<MonthlyCashMonth>();
        var running = opening;
        var projected = opening;

        foreach (var competence in YearMonth.Range(start, end))
        {
            // O saldo inicial entra no mês a que se refere, antes de qualquer
            // movimento dele: é o dinheiro que já estava lá quando o mês abriu.
            if (declaredIn == competence && declared is { } starting)
            {
                running += starting.Amount;
                projected += starting.Amount;
            }

            // Mês sem lançamento nenhum continua na lista: um buraco no meio da
            // série esconderia justamente o mês em que nada entrou.
            var rows = byMonth.TryGetValue(competence, out var found) ? found : [];

            var income = Sum(rows, EntryType.Income);
            var expense = Sum(rows, EntryType.Expense);
            var monthOpening = running;
            running += income - expense;

            // Mês passado não tem previsto: o que aconteceu, aconteceu. Só do
            // mês corrente em diante faz sentido esperar mais alguma coisa.
            var isFuture = competence >= current;

            var (expectedIncome, expectedExpense) = isFuture
                ? Expected(competence, forecastByMonth, recurring, generated)
                : (0m, 0m);

            projected += income - expense + expectedIncome - expectedExpense;

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
                IsForecast: competence > current,
                ExpectedIncome: expectedIncome,
                ExpectedExpense: expectedExpense,
                ProjectedResult: income - expense + expectedIncome - expectedExpense,
                ProjectedBalance: projected,
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

        // Com um único mês movimentado, "melhor" e "pior" caem na mesma linha e
        // marcam o mesmo mês como os dois — um superlativo sem nada com que
        // comparar não informa nada, então nenhum dos dois aparece.
        var best = IndexOfExtreme(months, best: true);
        var worst = IndexOfExtreme(months, best: false);
        if (best == worst) (best, worst) = (-1, -1);

        return new MonthlyCashResponse(
            start.ToString(),
            end.ToString(),
            opening,
            totalIncome,
            totalExpense,
            totalIncome - totalExpense,
            running,
            AverageMonthlyResult: Average(months, totalIncome - totalExpense),
            TotalFixedExpense: months.Sum(m => m.FixedExpense),
            TotalProLabore: months.Sum(m => m.ProLabore),
            ProjectedBalance: projected,
            TotalExpectedIncome: months.Sum(m => m.ExpectedIncome),
            TotalExpectedExpense: months.Sum(m => m.ExpectedExpense),
            OverdueReceivable: overdue.Receivable,
            OverduePayable: overdue.Payable,
            BestMonthIndex: best,
            WorstMonthIndex: worst,
            Opening: declared,
            Months: months);
    }

    /// <summary>
    /// O previsto de um mês: as parcelas em aberto que vencem nele mais os itens
    /// fixos vigentes que ainda não viraram lançamento. Descontar os já gerados
    /// é o que impede contar o aluguel duas vezes no mês em que ele já foi lançado.
    /// </summary>
    private static (decimal Income, decimal Expense) Expected(
        YearMonth competence,
        Dictionary<YearMonth, List<InstallmentForecastBucket>> forecastByMonth,
        IReadOnlyList<RecurringItem> recurring,
        IReadOnlySet<(Guid RecurringItemId, DateOnly Month)> generated)
    {
        var installments = forecastByMonth.TryGetValue(competence, out var found) ? found : [];

        var income = installments
            .Where(f => f.Direction == ContractDirection.Receivable)
            .Sum(f => f.Amount);

        var expense = installments
            .Where(f => f.Direction == ContractDirection.Payable)
            .Sum(f => f.Amount);

        var pending = recurring
            .Where(item => item.IsDueIn(competence))
            .Where(item => !generated.Contains((item.Id, competence.FirstDay)))
            .ToList();

        income += pending.Where(i => i.EntryType == EntryType.Income).Sum(i => i.Amount);
        expense += pending.Where(i => i.EntryType == EntryType.Expense).Sum(i => i.Amount);

        return (income, expense);
    }

    private static decimal Average(List<MonthlyCashMonth> months, decimal result) =>
        months.Count == 0 ? 0m : decimal.Round(result / months.Count, 2, MidpointRounding.AwayFromZero);

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
