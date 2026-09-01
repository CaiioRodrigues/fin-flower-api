using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class EntryTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Event NewEvent() => new(Guid.CreateVersion7(), "Festa", null, Today);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Amount_must_be_positive(decimal amount)
    {
        var @event = NewEvent();

        var act = () => @event.AddEntry(EntryType.Income, "Ingressos", amount, "Vendas", Today);

        act.Should().Throw<DomainException>().WithMessage("*maior que zero*");
    }

    [Fact]
    public void Amount_is_rounded_to_two_decimal_places()
    {
        var @event = NewEvent();

        var entry = @event.AddEntry(EntryType.Expense, "Taxa", 10.005m, "Outros", Today);

        entry.Amount.Should().Be(10.01m, "dinheiro tem duas casas e arredonda para cima no meio");
    }

    [Fact]
    public void Signed_amount_reflects_the_entry_type()
    {
        var @event = NewEvent();

        var income = @event.AddEntry(EntryType.Income, "Ingressos", 100m, "Vendas", Today);
        var expense = @event.AddEntry(EntryType.Expense, "Buffet", 40m, "Alimentação", Today);

        income.SignedAmount.Should().Be(100m);
        expense.SignedAmount.Should().Be(-40m);
        expense.Amount.Should().Be(40m, "o valor armazenado é sempre positivo");
    }

    [Fact]
    public void Description_is_trimmed_and_required()
    {
        var @event = NewEvent();

        var entry = @event.AddEntry(EntryType.Income, "  Ingressos  ", 100m, "  Vendas  ", Today);

        entry.Description.Should().Be("Ingressos");
        entry.Category.Should().Be("Vendas");

        @event.Invoking(e => e.AddEntry(EntryType.Income, "   ", 100m, "Vendas", Today))
            .Should().Throw<DomainException>().WithMessage("*descrição*");
    }

    [Fact]
    public void Description_longer_than_the_limit_is_rejected()
    {
        var @event = NewEvent();
        var tooLong = new string('a', Entry.MaxDescriptionLength + 1);

        @event.Invoking(e => e.AddEntry(EntryType.Income, tooLong, 100m, "Vendas", Today))
            .Should().Throw<DomainException>().WithMessage("*no máximo*");
    }

    [Fact]
    public void Entry_can_be_updated_while_the_event_is_open()
    {
        var @event = NewEvent();
        var entry = @event.AddEntry(EntryType.Income, "Ingressos", 100m, "Vendas", Today);

        @event.UpdateEntry(entry.Id, EntryType.Expense, "Reembolso", 30m, "Outros", Today);

        entry.Type.Should().Be(EntryType.Expense);
        entry.Amount.Should().Be(30m);
        @event.Result.Should().Be(-30m);
    }
}
