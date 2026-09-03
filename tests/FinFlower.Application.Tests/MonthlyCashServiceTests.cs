using FinFlower.Application.Common;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// O caixa completo, mês a mês. É o número que o dono do negócio olha primeiro,
/// então o saldo acumulado precisa amarrar mês com mês sem buraco.
/// </summary>
public class MonthlyCashServiceTests
{
    private static Task<Result<LedgerEntryResponse>> Add(
        EventTestContext ctx,
        EntryType type,
        decimal amount,
        DateOnly on,
        string category = "Geral",
        Guid? eventId = null) =>
        ctx.Entries.CreateAsync(new CreateEntryRequest(type, "Lançamento", amount, category, on, eventId));

    [Fact]
    public async Task Each_month_opens_with_the_closing_balance_of_the_previous_one()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 10_000m, new DateOnly(2026, 7, 10));
        await Add(ctx, EntryType.Expense, 4_000m, new DateOnly(2026, 7, 20));
        await Add(ctx, EntryType.Expense, 2_000m, new DateOnly(2026, 8, 5));
        await Add(ctx, EntryType.Income, 3_000m, new DateOnly(2026, 9, 1));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-07", "2026-09")).Value;

        cash.Months.Should().HaveCount(3);

        cash.Months[0].OpeningBalance.Should().Be(0m);
        cash.Months[0].Result.Should().Be(6_000m);
        cash.Months[0].ClosingBalance.Should().Be(6_000m);

        cash.Months[1].OpeningBalance.Should().Be(6_000m, "abre com o fechamento de julho");
        cash.Months[1].Result.Should().Be(-2_000m);
        cash.Months[1].ClosingBalance.Should().Be(4_000m);

        cash.Months[2].ClosingBalance.Should().Be(7_000m);
        cash.ClosingBalance.Should().Be(7_000m);
        cash.Result.Should().Be(7_000m);
    }

    [Fact]
    public async Task Balance_from_before_the_window_is_carried_in()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 5_000m, new DateOnly(2026, 3, 10));
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 8, 10));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-08", "2026-08")).Value;

        // Sem isso o primeiro mês da tela começaria do zero e o saldo mentiria.
        cash.OpeningBalance.Should().Be(5_000m);
        cash.Months.Single().ClosingBalance.Should().Be(6_000m);
    }

    [Fact]
    public async Task Months_without_movement_stay_in_the_series()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 7, 1));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-07", "2026-10")).Value;

        // Um buraco no meio da série esconderia justamente o mês em que nada entrou.
        cash.Months.Select(m => m.Competence)
            .Should().Equal("2026-07", "2026-08", "2026-09", "2026-10");
        cash.Months[2].EntryCount.Should().Be(0);
        cash.Months[2].ClosingBalance.Should().Be(1_000m, "o saldo não some, só não muda");
    }

    [Fact]
    public async Task Fixed_costs_and_pro_labore_are_broken_out_of_the_month()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await ctx.RecurringItems.CreateAsync(new CreateRecurringItemRequest(
            RecurringKind.FixedExpense, "Aluguel", 2_500m, "Estrutura", 10, "2026-01", null, null));
        await ctx.RecurringItems.CreateAsync(new CreateRecurringItemRequest(
            RecurringKind.ProLabore, "Retirada do sócio", 6_000m, "Sócios", 5, "2026-01", null, null));

        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);
        await Add(ctx, EntryType.Expense, 800m, new DateOnly(2026, 9, 12), "Marketing");

        var month = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value.Months.Single();

        month.Expense.Should().Be(9_300m);
        month.FixedExpense.Should().Be(2_500m);
        month.ProLabore.Should().Be(6_000m, "retirada de sócio não é custo do negócio");
    }

    [Fact]
    public async Task What_belongs_to_an_event_is_separated_from_what_does_not()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(
            new CreateEventRequest("Casamento Silva", null, new DateOnly(2026, 9, 20)))).Value;

        await Add(ctx, EntryType.Income, 15_000m, new DateOnly(2026, 9, 20), "Serviços", @event.Id);
        await Add(ctx, EntryType.Expense, 4_000m, new DateOnly(2026, 9, 18), "Fornecedores", @event.Id);
        await Add(ctx, EntryType.Expense, 1_200m, new DateOnly(2026, 9, 5), "Escritório");

        var month = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value.Months.Single();

        month.EventIncome.Should().Be(15_000m);
        month.EventExpense.Should().Be(4_000m);
        month.Expense.Should().Be(5_200m, "o gasto sem evento continua sendo saída do caixa");
    }

    [Fact]
    public async Task Categories_are_totalled_and_ordered_by_weight()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Expense, 300m, new DateOnly(2026, 9, 2), "Marketing");
        await Add(ctx, EntryType.Expense, 900m, new DateOnly(2026, 9, 8), "Fornecedores");
        await Add(ctx, EntryType.Expense, 200m, new DateOnly(2026, 9, 9), "Marketing");

        var month = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value.Months.Single();

        month.ExpenseByCategory.Select(c => c.Category).Should().Equal("Fornecedores", "Marketing");
        month.ExpenseByCategory.Single(c => c.Category == "Marketing").Amount.Should().Be(500m);
        month.ExpenseByCategory.Single(c => c.Category == "Marketing").Count.Should().Be(2);
    }

    [Fact]
    public async Task Best_and_worst_months_ignore_the_empty_ones()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 7, 5));
        await Add(ctx, EntryType.Expense, 4_000m, new DateOnly(2026, 8, 5));
        await Add(ctx, EntryType.Income, 9_000m, new DateOnly(2026, 10, 5));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-07", "2026-10")).Value;

        cash.Months[cash.BestMonthIndex].Competence.Should().Be("2026-10");
        cash.Months[cash.WorstMonthIndex].Competence.Should().Be("2026-08");
    }

    [Fact]
    public async Task Default_window_is_the_twelve_months_ending_now()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        // O relógio de teste marca 01/09/2026.
        var cash = (await ctx.MonthlyCash.GetAsync(null, null)).Value;

        cash.From.Should().Be("2025-10");
        cash.To.Should().Be("2026-09");
        cash.Months.Should().HaveCount(12);
    }

    [Fact]
    public async Task Only_one_side_of_the_range_anchors_the_default_window()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var fromOnly = (await ctx.MonthlyCash.GetAsync("2026-01", null)).Value;
        var toOnly = (await ctx.MonthlyCash.GetAsync(null, "2026-06")).Value;

        fromOnly.To.Should().Be("2026-12");
        toOnly.From.Should().Be("2025-07");
    }

    [Fact]
    public async Task An_inverted_range_is_read_the_right_way_round()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-07")).Value;

        cash.From.Should().Be("2026-07");
        cash.To.Should().Be("2026-09");
    }

    [Fact]
    public async Task A_range_that_is_too_wide_is_refused()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var result = await ctx.MonthlyCash.GetAsync("2010-01", "2030-01");

        result.Error!.Code.Should().Be("cash.range_too_wide");
    }

    [Fact]
    public async Task A_malformed_competence_is_refused()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var result = await ctx.MonthlyCash.GetAsync("setembro", null);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error!.Message.Should().Contain("aaaa-mm");
    }

    [Fact]
    public async Task Another_users_money_never_appears()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await Add(ctx, EntryType.Income, 50_000m, new DateOnly(2026, 9, 1));

        ctx.ActAs();
        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value;

        cash.OpeningBalance.Should().Be(0m);
        cash.ClosingBalance.Should().Be(0m);
        cash.Months.Single().EntryCount.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_session_the_cash_is_unreachable()
    {
        using var ctx = new EventTestContext();
        ctx.CurrentUser.UserId = null;

        (await ctx.MonthlyCash.GetAsync(null, null)).Error!.Type.Should().Be(ErrorType.Unauthorized);
    }
}
