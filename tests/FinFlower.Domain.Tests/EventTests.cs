using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class EventTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Event NewEvent() => new(Guid.CreateVersion7(), "Festa de Ano Novo", null, Today);

    [Fact]
    public void Result_is_income_minus_expense()
    {
        var @event = NewEvent();
        @event.AddEntry(EntryType.Income, "Ingressos", 8000m, "Vendas", Today);
        @event.AddEntry(EntryType.Expense, "Aluguel do espaço", 3000m, "Estrutura", Today);
        @event.AddEntry(EntryType.Expense, "Buffet", 2500m, "Alimentação", Today);

        @event.TotalIncome.Should().Be(8000m);
        @event.TotalExpense.Should().Be(5500m);
        @event.Result.Should().Be(2500m);
        @event.IsProfitable.Should().BeTrue();
    }

    [Fact]
    public void Result_is_negative_when_event_loses_money()
    {
        var @event = NewEvent();
        @event.AddEntry(EntryType.Income, "Ingressos", 1000m, "Vendas", Today);
        @event.AddEntry(EntryType.Expense, "Estrutura", 4000m, "Estrutura", Today);

        @event.Result.Should().Be(-3000m);
        @event.IsProfitable.Should().BeFalse();
    }

    [Fact]
    public void Removed_entries_are_excluded_from_totals()
    {
        var @event = NewEvent();
        var entry = @event.AddEntry(EntryType.Expense, "Lançamento errado", 500m, "Outros", Today);
        @event.AddEntry(EntryType.Expense, "Buffet", 200m, "Alimentação", Today);

        @event.RemoveEntry(entry.Id, Now);

        @event.TotalExpense.Should().Be(200m);
        @event.Entries.Should().HaveCount(2, "a exclusão é lógica: o registro continua no banco");
    }

    [Fact]
    public void Closed_event_rejects_new_entries()
    {
        var @event = NewEvent();
        @event.Close();

        var act = () => @event.AddEntry(EntryType.Income, "Ingressos", 100m, "Vendas", Today);

        act.Should().Throw<DomainException>().WithMessage("*evento fechado*");
    }

    [Fact]
    public void Closed_event_rejects_updates_and_removals()
    {
        var @event = NewEvent();
        var entry = @event.AddEntry(EntryType.Income, "Ingressos", 100m, "Vendas", Today);
        @event.Close();

        @event.Invoking(e => e.UpdateDetails("Outro nome", null, Today))
            .Should().Throw<DomainException>();

        @event.Invoking(e => e.UpdateEntry(entry.Id, EntryType.Income, "x", 1m, "Vendas", Today))
            .Should().Throw<DomainException>();

        @event.Invoking(e => e.RemoveEntry(entry.Id, Now))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Reopened_event_accepts_entries_again()
    {
        var @event = NewEvent();
        @event.Close();
        @event.Reopen();

        @event.AddEntry(EntryType.Income, "Ingressos", 100m, "Vendas", Today);

        @event.Status.Should().Be(EventStatus.Open);
        @event.TotalIncome.Should().Be(100m);
    }

    [Fact]
    public void Closing_twice_is_rejected()
    {
        var @event = NewEvent();
        @event.Close();

        @event.Invoking(e => e.Close()).Should().Throw<DomainException>();
    }

    [Fact]
    public void Updating_an_unknown_entry_is_rejected()
    {
        var @event = NewEvent();

        @event.Invoking(e => e.UpdateEntry(Guid.CreateVersion7(), EntryType.Income, "x", 1m, "Vendas", Today))
            .Should().Throw<DomainException>().WithMessage("*não encontrado*");
    }

    [Fact]
    public void Event_requires_an_owner()
    {
        var act = () => new Event(Guid.Empty, "Festa", null, Today);

        act.Should().Throw<DomainException>().WithMessage("*dono*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Event_requires_a_name(string name)
    {
        var act = () => new Event(Guid.CreateVersion7(), name, null, Today);

        act.Should().Throw<DomainException>().WithMessage("*nome*");
    }
}
