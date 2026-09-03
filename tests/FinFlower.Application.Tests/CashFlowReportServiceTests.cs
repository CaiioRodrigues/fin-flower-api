using FinFlower.Application.Common;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

public class CashFlowReportServiceTests
{
    // O relógio de teste marca 01/09/2026.
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static async Task<Guid> NewEventAsync(EventTestContext ctx, string name = "Festa") =>
        (await ctx.Events.CreateAsync(new CreateEventRequest(name, null, new DateOnly(2026, 12, 12)))).Value.Id;

    private static Task<Result<ContractResponse>> NewContractAsync(
        EventTestContext ctx,
        Guid eventId,
        ContractDirection direction,
        decimal total,
        int parts,
        DateOnly firstDue) =>
        ctx.Contracts.CreateAsync(new CreateContractRequest(
            direction, direction == ContractDirection.Receivable ? "Cliente" : "Fornecedor",
            null, total, PaymentMethod.Boleto, parts, firstDue, Today, eventId));

    [Fact]
    public async Task Separates_what_is_overdue_from_the_current_month_and_the_next_ones()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx);

        // Vencida em agosto, uma em setembro (mês corrente) e duas nos próximos.
        await NewContractAsync(ctx, eventId, ContractDirection.Receivable, 1000m, 1, new DateOnly(2026, 8, 10));
        await NewContractAsync(ctx, eventId, ContractDirection.Receivable, 3000m, 3, new DateOnly(2026, 9, 20));

        var report = (await ctx.CashFlow.GetAsync(monthsAhead: 3)).Value;

        report.Overdue.Receivable.Should().Be(1000m);
        report.Overdue.InstallmentCount.Should().Be(1);

        report.CurrentMonth.Year.Should().Be(2026);
        report.CurrentMonth.Month.Should().Be(9);
        report.CurrentMonth.Receivable.Should().Be(1000m);

        report.UpcomingMonths.Should().HaveCount(3);
        report.UpcomingMonths[0].Month.Should().Be(10);
        report.UpcomingMonths[0].Receivable.Should().Be(1000m);
        report.UpcomingMonths[1].Receivable.Should().Be(1000m);
        report.UpcomingMonths[2].Receivable.Should().Be(0m, "novembro não tem parcela");
    }

    [Fact]
    public async Task Receivables_and_payables_are_reported_side_by_side()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx);

        await NewContractAsync(ctx, eventId, ContractDirection.Receivable, 10_000m, 1, new DateOnly(2026, 10, 5));
        await NewContractAsync(ctx, eventId, ContractDirection.Payable, 4000m, 1, new DateOnly(2026, 10, 20));

        var report = (await ctx.CashFlow.GetAsync(monthsAhead: 2)).Value;
        var october = report.UpcomingMonths.Single(m => m.Month == 10);

        october.Receivable.Should().Be(10_000m);
        october.Payable.Should().Be(4000m);
        october.Net.Should().Be(6000m);
        october.InstallmentCount.Should().Be(2);

        report.TotalReceivable.Should().Be(10_000m);
        report.TotalPayable.Should().Be(4000m);
    }

    [Fact]
    public async Task Projected_balance_joins_what_is_realized_with_what_is_forecast()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx);

        // Realizado: uma despesa lançada à mão.
        await ctx.Entries.CreateAsync(new CreateEntryRequest(
            EntryType.Expense, "Sinal do espaço", 2000m, "Estrutura", Today, eventId));

        await NewContractAsync(ctx, eventId, ContractDirection.Receivable, 10_000m, 1, new DateOnly(2026, 10, 5));
        await NewContractAsync(ctx, eventId, ContractDirection.Payable, 3000m, 1, new DateOnly(2026, 11, 5));

        var report = (await ctx.CashFlow.GetAsync(monthsAhead: 6)).Value;

        report.RealizedBalance.Should().Be(-2000m);
        report.ProjectedBalance.Should().Be(5000m, "-2000 realizado + 10000 a receber - 3000 a pagar");
    }

    [Fact]
    public async Task A_settled_installment_moves_from_forecast_to_realized()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx);
        var contract = (await NewContractAsync(
            ctx, eventId, ContractDirection.Receivable, 6000m, 2, new DateOnly(2026, 10, 5))).Value;

        var before = (await ctx.CashFlow.GetAsync(6)).Value;
        before.TotalReceivable.Should().Be(6000m);
        before.RealizedBalance.Should().Be(0m);

        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest());
        var after = (await ctx.CashFlow.GetAsync(6)).Value;

        after.TotalReceivable.Should().Be(3000m, "a parcela liquidada sai do previsto");
        after.RealizedBalance.Should().Be(3000m, "e entra no realizado");
        after.ProjectedBalance.Should().Be(6000m, "o total projetado não muda ao receber");
    }

    [Fact]
    public async Task Canceled_installments_leave_the_forecast()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx);
        var contract = (await NewContractAsync(
            ctx, eventId, ContractDirection.Receivable, 6000m, 2, new DateOnly(2026, 10, 5))).Value;

        await ctx.Contracts.CancelInstallmentAsync(contract.Id, 2);
        var report = (await ctx.CashFlow.GetAsync(6)).Value;

        report.TotalReceivable.Should().Be(3000m);
    }

    [Fact]
    public async Task The_overdue_list_identifies_event_and_counterparty()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx, "Show de rock");
        await NewContractAsync(ctx, eventId, ContractDirection.Receivable, 1500m, 1, new DateOnly(2026, 7, 1));

        var report = (await ctx.CashFlow.GetAsync(6)).Value;

        var overdue = report.Overdues.Should().ContainSingle().Subject;
        overdue.EventName.Should().Be("Show de rock");
        overdue.Counterparty.Should().Be("Cliente");
        overdue.Amount.Should().Be(1500m);
        overdue.IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task Another_users_installments_never_enter_the_report()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var eventId = await NewEventAsync(ctx);
        await NewContractAsync(ctx, eventId, ContractDirection.Receivable, 10_000m, 2, new DateOnly(2026, 10, 5));

        ctx.ActAs();
        var report = (await ctx.CashFlow.GetAsync(6)).Value;

        report.TotalReceivable.Should().Be(0m);
        report.ProjectedBalance.Should().Be(0m);
        report.Overdues.Should().BeEmpty();
    }

    [Fact]
    public async Task An_out_of_range_horizon_is_rejected()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var result = await ctx.CashFlow.GetAsync(monthsAhead: 99);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("report.invalid_horizon");
    }
}
