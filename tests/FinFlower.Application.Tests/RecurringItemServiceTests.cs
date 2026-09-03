using FinFlower.Application.Common;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// Gastos fixos e pró-labore. O ponto delicado é a geração do mês: rodar duas
/// vezes não pode duplicar a despesa, porque quem opera vai clicar de novo.
/// </summary>
public class RecurringItemServiceTests
{
    private static Task<Result<RecurringItemResponse>> NewItem(
        EventTestContext ctx,
        RecurringKind kind = RecurringKind.FixedExpense,
        string description = "Aluguel do galpão",
        decimal amount = 2_500m,
        int dayOfMonth = 10,
        string start = "2026-01",
        string? end = null) =>
        ctx.RecurringItems.CreateAsync(new CreateRecurringItemRequest(
            kind, description, amount, "Estrutura", dayOfMonth, start, end, null));

    [Fact]
    public async Task Generating_a_month_turns_the_items_into_ledger_entries()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx, dayOfMonth: 10);
        await NewItem(ctx, RecurringKind.ProLabore, "Retirada do sócio", 6_000m, 5);

        var generated = (await ctx.RecurringItems.GenerateMonthAsync("2026-09", null)).Value;

        generated.Generated.Should().Be(2);
        generated.GeneratedAmount.Should().Be(8_500m);

        var ledger = (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value;
        ledger.Entries.Should().HaveCount(2);
        ledger.Entries.Should().AllSatisfy(e => e.Source.Should().Be(EntrySource.Recurring));
        ledger.Entries.Select(e => e.OccurredOn)
            .Should().BeEquivalentTo([new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 5)]);
    }

    [Fact]
    public async Task Generating_the_same_month_twice_does_not_duplicate()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx);

        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);
        var second = (await ctx.RecurringItems.GenerateMonthAsync("2026-09", null)).Value;

        second.Generated.Should().Be(0);
        second.AlreadyExisted.Should().Be(1);
        (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Each_month_is_generated_on_its_own()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx);

        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);
        await ctx.RecurringItems.GenerateMonthAsync("2026-10", null);

        var ledger = (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value;
        ledger.TotalCount.Should().Be(2);
        ledger.Entries.Select(e => e.Competence).Should().BeEquivalentTo(["2026-09", "2026-10"]);
    }

    [Fact]
    public async Task Only_the_chosen_items_are_generated_when_ids_are_given()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var rent = (await NewItem(ctx)).Value;
        await NewItem(ctx, RecurringKind.ProLabore, "Retirada do sócio", 6_000m, 5);

        var generated = (await ctx.RecurringItems.GenerateMonthAsync("2026-09", [rent.Id])).Value;

        generated.Generated.Should().Be(1);
        generated.Descriptions.Should().Equal("Aluguel do galpão");
    }

    [Fact]
    public async Task An_item_outside_its_period_is_not_generated()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx, start: "2026-10");

        var generated = (await ctx.RecurringItems.GenerateMonthAsync("2026-09", null)).Value;

        generated.Generated.Should().Be(0);
        generated.AlreadyExisted.Should().Be(0, "não é que já existia: nem era devido");
    }

    [Fact]
    public async Task A_deactivated_item_stops_being_generated_but_keeps_its_history()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var item = (await NewItem(ctx)).Value;
        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);

        await ctx.RecurringItems.SetActiveAsync(item.Id, active: false);
        var october = (await ctx.RecurringItems.GenerateMonthAsync("2026-10", null)).Value;

        october.Generated.Should().Be(0);
        (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value.TotalCount
            .Should().Be(1, "o aluguel de setembro foi pago de verdade");
    }

    [Fact]
    public async Task The_month_view_says_what_is_still_pending()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var rent = (await NewItem(ctx)).Value;
        await NewItem(ctx, RecurringKind.ProLabore, "Retirada do sócio", 6_000m, 5);

        await ctx.RecurringItems.GenerateMonthAsync("2026-09", [rent.Id]);
        var month = (await ctx.RecurringItems.ListAsync(new RecurringFilter(), "2026-09")).Value;

        month.TotalFixedExpense.Should().Be(2_500m);
        month.TotalProLabore.Should().Be(6_000m);
        month.PendingAmount.Should().Be(6_000m);
        month.PendingCount.Should().Be(1);
        month.Items.Single(i => i.Id == rent.Id).GeneratedForMonth.Should().BeTrue();
    }

    [Fact]
    public async Task The_month_total_ignores_items_that_do_not_apply_yet()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx, start: "2026-01");
        await NewItem(ctx, description: "Software novo", amount: 400m, start: "2026-11");

        var september = (await ctx.RecurringItems.ListAsync(new RecurringFilter(), "2026-09")).Value;

        september.TotalFixedExpense.Should().Be(2_500m, "o software só começa em novembro");
        september.Items.Should().HaveCount(2, "mas o item aparece na tela, marcado como não devido");
        september.Items.Single(i => i.Description == "Software novo").DueInMonth.Should().BeFalse();
    }

    [Fact]
    public async Task The_kinds_are_listed_separately()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx);
        await NewItem(ctx, RecurringKind.ProLabore, "Retirada do sócio", 6_000m, 5);

        var proLabore = (await ctx.RecurringItems.ListAsync(
            new RecurringFilter(Kind: RecurringKind.ProLabore), "2026-09")).Value;

        proLabore.Items.Should().ContainSingle().Which.Description.Should().Be("Retirada do sócio");
    }

    [Fact]
    public async Task Raising_the_amount_does_not_rewrite_the_months_already_generated()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var item = (await NewItem(ctx)).Value;
        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);

        await ctx.RecurringItems.UpdateAsync(item.Id, new UpdateRecurringItemRequest(
            "Aluguel do galpão", 2_800m, "Estrutura", 10, null, "reajuste anual"));
        await ctx.RecurringItems.GenerateMonthAsync("2026-10", null);

        var ledger = (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value.Entries;
        ledger.Single(e => e.Competence == "2026-09").Amount.Should().Be(2_500m);
        ledger.Single(e => e.Competence == "2026-10").Amount.Should().Be(2_800m);
    }

    [Fact]
    public async Task A_generated_entry_can_be_corrected_by_hand()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewItem(ctx, description: "Conta de luz", amount: 480m);
        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);
        var entry = (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value.Entries.Single();

        // A conta veio diferente do previsto: corrigir é o uso normal.
        entry.IsEditable.Should().BeTrue();
        var updated = await ctx.Entries.UpdateAsync(entry.Id, new UpdateEntryRequest(
            EntryType.Expense, "Conta de luz", 512.30m, "Estrutura", entry.OccurredOn));

        updated.Value.Amount.Should().Be(512.30m);
    }

    [Fact]
    public async Task Deleting_an_item_keeps_what_it_already_generated()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var item = (await NewItem(ctx)).Value;
        await ctx.RecurringItems.GenerateMonthAsync("2026-09", null);

        await ctx.RecurringItems.DeleteAsync(item.Id);

        (await ctx.RecurringItems.ListAsync(new RecurringFilter(), "2026-09")).Value.Items.Should().BeEmpty();
        (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task A_malformed_competence_is_refused()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var result = await ctx.RecurringItems.GenerateMonthAsync("13/2026", null);

        result.Error!.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task An_invalid_day_of_month_is_refused_by_the_domain()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var act = async () => await NewItem(ctx, dayOfMonth: 45);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*dia do vencimento*");
    }

    [Fact]
    public async Task Another_users_items_are_invisible_and_untouchable()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var item = (await NewItem(ctx)).Value;

        ctx.ActAs();
        var list = (await ctx.RecurringItems.ListAsync(new RecurringFilter(), "2026-09")).Value;
        var update = await ctx.RecurringItems.UpdateAsync(item.Id, new UpdateRecurringItemRequest(
            "Sequestrado", 1m, "Outros", 1, null, null));
        var generated = (await ctx.RecurringItems.GenerateMonthAsync("2026-09", null)).Value;

        list.Items.Should().BeEmpty();
        update.Error!.Type.Should().Be(ErrorType.NotFound);
        generated.Generated.Should().Be(0);
    }
}
