using FinFlower.Application.Common;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

public class CashReportServiceTests
{
    /// <summary>Cria um evento na data indicada com uma entrada e uma saída.</summary>
    private static async Task CreateEventAsync(
        EventTestContext ctx,
        string name,
        DateOnly date,
        decimal income,
        decimal expense)
    {
        var @event = (await ctx.Events.CreateAsync(new CreateEventRequest(name, null, date))).Value;

        if (income > 0)
            await ctx.Events.AddEntryAsync(@event.Id, new CreateEntryRequest(EntryType.Income, "Ingressos", income, "Vendas", date));

        if (expense > 0)
            await ctx.Events.AddEntryAsync(@event.Id, new CreateEntryRequest(EntryType.Expense, "Custos", expense, "Estrutura", date));
    }

    /// <summary>O cenário do enunciado: cinco eventos, três com lucro e dois com prejuízo.</summary>
    private static async Task ArrangeFiveEventsAsync(EventTestContext ctx)
    {
        await CreateEventAsync(ctx, "Festa junina", new DateOnly(2026, 6, 20), 5000m, 2000m);   // +3000
        await CreateEventAsync(ctx, "Show de rock", new DateOnly(2026, 7, 10), 12000m, 7000m);  // +5000
        await CreateEventAsync(ctx, "Feira gastronômica", new DateOnly(2026, 8, 5), 3000m, 4500m); // -1500
        await CreateEventAsync(ctx, "Workshop", new DateOnly(2026, 8, 22), 800m, 2300m);        // -1500
        await CreateEventAsync(ctx, "Réveillon", new DateOnly(2026, 12, 31), 20000m, 11000m);   // +9000
    }

    [Fact]
    public async Task Report_counts_profitable_and_unprofitable_events()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ArrangeFiveEventsAsync(ctx);

        var report = (await ctx.CashReport.GetAsync(from: null, to: null)).Value;

        report.EventCount.Should().Be(5);
        report.ProfitableEventCount.Should().Be(3);
        report.UnprofitableEventCount.Should().Be(2);
        report.BreakEvenEventCount.Should().Be(0);
    }

    [Fact]
    public async Task Balance_is_total_income_minus_total_expense()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ArrangeFiveEventsAsync(ctx);

        var report = (await ctx.CashReport.GetAsync(from: null, to: null)).Value;

        report.TotalIncome.Should().Be(40_800m);
        report.TotalExpense.Should().Be(26_800m);
        report.Balance.Should().Be(14_000m);
        report.Balance.Should().Be(report.Events.Sum(e => e.Result), "o caixa é a soma dos resultados dos eventos");
    }

    [Fact]
    public async Task Each_line_carries_the_result_of_its_event()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ArrangeFiveEventsAsync(ctx);

        var report = (await ctx.CashReport.GetAsync(from: null, to: null)).Value;

        var show = report.Events.Single(e => e.Name == "Show de rock");
        show.TotalIncome.Should().Be(12_000m);
        show.TotalExpense.Should().Be(7000m);
        show.Result.Should().Be(5000m);
        show.IsProfitable.Should().BeTrue();

        report.Events.Single(e => e.Name == "Workshop").IsProfitable.Should().BeFalse();
    }

    [Fact]
    public async Task Report_can_be_limited_to_a_period()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ArrangeFiveEventsAsync(ctx);

        var august = (await ctx.CashReport.GetAsync(
            from: new DateOnly(2026, 8, 1),
            to: new DateOnly(2026, 8, 31))).Value;

        august.EventCount.Should().Be(2);
        august.ProfitableEventCount.Should().Be(0);
        august.Balance.Should().Be(-3000m);
    }

    [Fact]
    public async Task An_event_that_breaks_even_is_neither_profit_nor_loss()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await CreateEventAsync(ctx, "Evento no zero a zero", new DateOnly(2026, 5, 1), 1000m, 1000m);

        var report = (await ctx.CashReport.GetAsync(null, null)).Value;

        report.BreakEvenEventCount.Should().Be(1);
        report.ProfitableEventCount.Should().Be(0);
        report.UnprofitableEventCount.Should().Be(0);
        report.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Event_without_entries_counts_but_moves_nothing()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ctx.Events.CreateAsync(new CreateEventRequest("Evento planejado", null, new DateOnly(2026, 10, 1)));

        var report = (await ctx.CashReport.GetAsync(null, null)).Value;

        report.EventCount.Should().Be(1);
        report.TotalIncome.Should().Be(0m);
        report.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task Deleted_event_leaves_the_cash_report()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ArrangeFiveEventsAsync(ctx);
        var show = (await ctx.Events.ListAsync(new EventFilter())).Value.Single(e => e.Name == "Show de rock");

        await ctx.Events.DeleteAsync(show.Id);
        var report = (await ctx.CashReport.GetAsync(null, null)).Value;

        report.EventCount.Should().Be(4);
        report.Balance.Should().Be(9000m, "o resultado de +5000 do show sai da conta");
    }

    [Fact]
    public async Task Closed_events_still_count_in_the_cash_report()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ArrangeFiveEventsAsync(ctx);
        var list = (await ctx.Events.ListAsync(new EventFilter())).Value;
        foreach (var @event in list) await ctx.Events.CloseAsync(@event.Id);

        var report = (await ctx.CashReport.GetAsync(null, null)).Value;

        report.EventCount.Should().Be(5);
        report.Balance.Should().Be(14_000m, "fechar consolida o resultado, não o remove do caixa");
    }

    [Fact]
    public async Task Inverted_period_is_rejected()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var result = await ctx.CashReport.GetAsync(
            from: new DateOnly(2026, 12, 1),
            to: new DateOnly(2026, 1, 1));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
    }
}
