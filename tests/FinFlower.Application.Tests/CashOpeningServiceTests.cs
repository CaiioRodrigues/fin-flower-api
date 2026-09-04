using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Common;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// O saldo inicial é o que separa "saldo" de "variação desde que eu comecei a
/// digitar". Errar aqui não desalinha uma tela: faz todo número de caixa do
/// sistema mentir pelo mesmo valor, em silêncio.
/// </summary>
public class CashOpeningServiceTests
{
    private static Task<Result<LedgerEntryResponse>> Add(
        EventTestContext ctx,
        EntryType type,
        decimal amount,
        DateOnly on) =>
        ctx.Entries.CreateAsync(new CreateEntryRequest(type, "Lançamento", amount, "Geral", on, null));

    private static SaveCashOpeningRequest Opening(decimal amount, DateOnly on, string? notes = null) =>
        new(amount, on, notes);

    [Fact]
    public async Task Without_a_declared_opening_the_balance_is_only_what_was_typed()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value;

        cash.Opening.Should().BeNull();
        cash.ClosingBalance.Should().Be(1_000m);
    }

    [Fact]
    public async Task The_declared_balance_is_where_the_cash_starts()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)));
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));
        await Add(ctx, EntryType.Expense, 400m, new DateOnly(2026, 9, 20));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value;

        // Setembro abre com os 30 mil que já existiam, não com zero.
        cash.Months[0].OpeningBalance.Should().Be(30_000m);
        cash.Months[0].Result.Should().Be(600m);
        cash.Months[0].ClosingBalance.Should().Be(30_600m);
        cash.ClosingBalance.Should().Be(30_600m);
    }

    [Fact]
    public async Task A_window_that_starts_after_the_declaration_carries_the_balance_in()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        // O lançamento de julho é o que denuncia o erro: ele já está dentro dos
        // 30 mil, e uma janela que começa depois da declaração precisa somar o
        // saldo declarado mais o que veio dele para cá — nunca o que veio antes.
        await Add(ctx, EntryType.Income, 8_000m, new DateOnly(2026, 7, 15));
        await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)));
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));
        await Add(ctx, EntryType.Expense, 500m, new DateOnly(2026, 10, 5));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-10", "2026-10")).Value;

        // Outubro não vê a declaração de setembro, mas herda o saldo que ela deixou.
        cash.OpeningBalance.Should().Be(31_000m);
        cash.Months[0].ClosingBalance.Should().Be(30_500m);
    }

    [Fact]
    public async Task Months_before_the_declaration_do_not_move_the_balance()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 8_000m, new DateOnly(2026, 7, 15));
        await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)));
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-07", "2026-09")).Value;

        // Julho é anterior à declaração: os 8 mil já estão dentro dos 30 mil, e
        // somá-los de novo contaria o mesmo dinheiro duas vezes.
        cash.Months[0].Income.Should().Be(0m);
        cash.Months[0].ClosingBalance.Should().Be(0m);
        cash.Months[2].OpeningBalance.Should().Be(30_000m);
        cash.ClosingBalance.Should().Be(31_000m);
    }

    [Fact]
    public async Task The_ignored_entries_are_counted_so_the_screen_can_explain_them()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 8_000m, new DateOnly(2026, 7, 15));
        await Add(ctx, EntryType.Expense, 200m, new DateOnly(2026, 8, 2));
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));

        var saved = (await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)))).Value;

        // Um lançamento que some da conta sem explicação parece defeito. Com o
        // número na mão, a tela consegue dizer que é regra.
        saved.IgnoredEntries.Should().Be(2);

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value;
        cash.Opening!.IgnoredEntries.Should().Be(2);
    }

    [Fact]
    public async Task Starting_in_the_red_is_allowed()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        // Recusar negativo obrigaria quem está no vermelho a mentir para o
        // próprio caixa — que é exatamente quem mais precisa enxergar o buraco.
        await ctx.CashOpening.SaveAsync(Opening(-2_500m, new DateOnly(2026, 9, 1)));
        await Add(ctx, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value;

        cash.Months[0].OpeningBalance.Should().Be(-2_500m);
        cash.ClosingBalance.Should().Be(-1_500m);
    }

    [Fact]
    public async Task Saving_twice_corrects_the_number_instead_of_adding_a_second_one()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)));
        await ctx.CashOpening.SaveAsync(Opening(28_400m, new DateOnly(2026, 9, 1), "conferido no extrato"));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value;

        cash.Opening!.Amount.Should().Be(28_400m);
        cash.Opening.Notes.Should().Be("conferido no extrato");
        cash.ClosingBalance.Should().Be(28_400m, "corrigir não soma um segundo saldo inicial");
    }

    [Fact]
    public async Task Clearing_gives_the_cash_back_its_full_history()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await Add(ctx, EntryType.Income, 8_000m, new DateOnly(2026, 7, 15));
        await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)));
        await ctx.CashOpening.ClearAsync();

        var cash = (await ctx.MonthlyCash.GetAsync("2026-07", "2026-09")).Value;

        cash.Opening.Should().BeNull();
        cash.Months[0].Income.Should().Be(8_000m, "sem o marco, julho volta para a conta");
        cash.ClosingBalance.Should().Be(8_000m);
    }

    [Fact]
    public async Task The_projection_starts_from_the_declared_balance_too()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        await ctx.CashOpening.SaveAsync(Opening(10_000m, new DateOnly(2026, 9, 1)));

        var cash = (await ctx.MonthlyCash.GetAsync("2026-09", "2026-10")).Value;

        // Um projetado que ignora o saldo inicial erra pelo mesmo valor que o
        // saldo — e é justamente o número usado para decidir se dá para pagar.
        cash.ProjectedBalance.Should().Be(10_000m);
    }

    [Fact]
    public async Task Another_owner_never_sees_the_declaration()
    {
        using var ctx = new EventTestContext();

        ctx.ActAs();
        await ctx.CashOpening.SaveAsync(Opening(30_000m, new DateOnly(2026, 9, 1)));

        ctx.ActAs(Guid.CreateVersion7());
        (await ctx.CashOpening.GetAsync()).Value.Should().BeNull();
        (await ctx.MonthlyCash.GetAsync("2026-09", "2026-09")).Value.ClosingBalance.Should().Be(0m);
    }
}
