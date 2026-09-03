using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class RecurringItemTests
{
    private static readonly YearMonth Start = new(2026, 1);

    private static RecurringItem NewItem(
        RecurringKind kind = RecurringKind.FixedExpense,
        int dayOfMonth = 10,
        YearMonth? end = null) =>
        new(Guid.CreateVersion7(), kind, "Aluguel do galpão", 2500m, "Estrutura", dayOfMonth, Start, end, null);

    [Fact]
    public void Fixed_expense_and_pro_labore_both_leave_the_cash_box()
    {
        NewItem(RecurringKind.FixedExpense).EntryType.Should().Be(EntryType.Expense);
        NewItem(RecurringKind.ProLabore).EntryType.Should().Be(EntryType.Expense);
        NewItem(RecurringKind.FixedIncome).EntryType.Should().Be(EntryType.Income);
    }

    [Fact]
    public void Item_is_due_only_inside_its_period()
    {
        var item = NewItem(end: new YearMonth(2026, 6));

        item.IsDueIn(new YearMonth(2025, 12)).Should().BeFalse("antes do início");
        item.IsDueIn(Start).Should().BeTrue("o mês inicial conta");
        item.IsDueIn(new YearMonth(2026, 6)).Should().BeTrue("o mês final conta");
        item.IsDueIn(new YearMonth(2026, 7)).Should().BeFalse("depois do fim");
    }

    [Fact]
    public void Item_without_an_end_never_stops()
    {
        NewItem().IsDueIn(new YearMonth(2099, 12)).Should().BeTrue();
    }

    [Fact]
    public void Deactivated_item_stops_being_due()
    {
        var item = NewItem();
        item.Deactivate();

        item.IsDueIn(new YearMonth(2026, 5)).Should().BeFalse();

        item.Activate();
        item.IsDueIn(new YearMonth(2026, 5)).Should().BeTrue();
    }

    [Fact]
    public void Generated_entry_carries_the_due_date_of_the_competence()
    {
        var item = NewItem(dayOfMonth: 5);

        var entry = item.GenerateEntry(new YearMonth(2026, 7));

        entry.OccurredOn.Should().Be(new DateOnly(2026, 7, 5));
        entry.Amount.Should().Be(2500m);
        entry.Type.Should().Be(EntryType.Expense);
        entry.RecurringMonth.Should().Be(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void Generated_entry_of_a_short_month_falls_on_the_last_day()
    {
        var entry = NewItem(dayOfMonth: 31).GenerateEntry(new YearMonth(2026, 2));

        entry.OccurredOn.Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void Generating_outside_the_period_is_rejected()
    {
        var item = NewItem(end: new YearMonth(2026, 3));

        item.Invoking(i => i.GenerateEntry(new YearMonth(2026, 4)))
            .Should().Throw<DomainException>().WithMessage("*não vale para a competência*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Day_of_month_must_be_a_real_day(int day)
    {
        var act = () => NewItem(dayOfMonth: day);

        act.Should().Throw<DomainException>().WithMessage("*dia do vencimento*");
    }

    [Fact]
    public void End_month_cannot_precede_the_start()
    {
        var act = () => NewItem(end: new YearMonth(2025, 12));

        act.Should().Throw<DomainException>().WithMessage("*anterior ao inicial*");
    }

    [Fact]
    public void Raising_the_amount_only_affects_what_has_not_been_generated()
    {
        var item = NewItem();
        var january = item.GenerateEntry(new YearMonth(2026, 1));

        item.UpdateDetails("Aluguel do galpão", 2800m, "Estrutura", 10, null, "reajuste anual");
        var february = item.GenerateEntry(new YearMonth(2026, 2));

        // O lançamento de janeiro já é um fato do caixa; o reajuste vale daqui
        // para a frente.
        january.Amount.Should().Be(2500m);
        february.Amount.Should().Be(2800m);
    }

    [Fact]
    public void Item_requires_an_owner()
    {
        var act = () => new RecurringItem(
            Guid.Empty, RecurringKind.ProLabore, "Retirada", 5000m, "Sócios", 5, Start, null, null);

        act.Should().Throw<DomainException>().WithMessage("*dono*");
    }
}
