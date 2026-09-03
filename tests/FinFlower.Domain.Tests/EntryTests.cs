using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class EntryTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static Entry NewEntry(
        EntryType type = EntryType.Income,
        decimal amount = 100m,
        Guid? eventId = null) =>
        new(Guid.CreateVersion7(), type, "Ingressos", amount, "Vendas", Today, eventId);

    [Fact]
    public void Entry_lives_in_the_ledger_without_an_event()
    {
        var entry = NewEntry();

        // O caixa existe sem evento: aluguel e pró-labore não pertencem a
        // trabalho nenhum, e continuam sendo dinheiro que sai.
        entry.EventId.Should().BeNull();
        entry.Source.Should().Be(EntrySource.Manual);
    }

    [Fact]
    public void Entry_requires_an_owner()
    {
        var act = () => new Entry(Guid.Empty, EntryType.Income, "Ingressos", 100m, "Vendas", Today);

        act.Should().Throw<DomainException>().WithMessage("*dono*");
    }

    [Fact]
    public void Signed_amount_carries_the_direction()
    {
        NewEntry(EntryType.Income, 250m).SignedAmount.Should().Be(250m);
        NewEntry(EntryType.Expense, 250m).SignedAmount.Should().Be(-250m);
    }

    [Fact]
    public void Competence_comes_from_the_day_the_money_moved()
    {
        var entry = NewEntry();

        entry.Competence.ToString().Should().Be("2026-09");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void Amount_must_be_positive(decimal amount)
    {
        var act = () => NewEntry(amount: amount);

        act.Should().Throw<DomainException>().WithMessage("*valor*");
    }

    [Fact]
    public void Amount_is_rounded_to_two_places()
    {
        NewEntry(amount: 10.005m).Amount.Should().Be(10.01m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Description_is_required(string description)
    {
        var act = () => new Entry(Guid.CreateVersion7(), EntryType.Income, description, 10m, "Vendas", Today);

        act.Should().Throw<DomainException>().WithMessage("*descrição*");
    }

    [Fact]
    public void Update_changes_every_field_including_the_event()
    {
        var entry = NewEntry();
        var eventId = Guid.CreateVersion7();

        entry.Update(EntryType.Expense, "Reembolso", 30m, "Outros", new DateOnly(2026, 10, 2), eventId);

        entry.Type.Should().Be(EntryType.Expense);
        entry.Amount.Should().Be(30m);
        entry.EventId.Should().Be(eventId);
        entry.Competence.ToString().Should().Be("2026-10");
    }

    [Fact]
    public void Entry_from_an_installment_belongs_to_the_contract()
    {
        // Passa pelo contrato, que é o único caminho público para criá-lo.
        var contract = new Contract(
            Guid.CreateVersion7(), ContractDirection.Receivable, "Prefeitura", null,
            9000m, PaymentMethod.Boleto, 3, new DateOnly(2026, 10, 5), Today);

        var entry = contract.SettleInstallment(1, Today, 3000m, null, "Contratos");

        entry.Source.Should().Be(EntrySource.Contract);
        entry.ComesFromContract.Should().BeTrue();

        // Alterar por fora quebraria o vínculo entre previsto e realizado.
        entry.Invoking(e => e.EnsureEditable())
            .Should().Throw<DomainException>().WithMessage("*Ajuste a parcela*");
    }

    [Fact]
    public void Entry_from_a_recurring_item_stays_editable()
    {
        var item = new RecurringItem(
            Guid.CreateVersion7(), RecurringKind.FixedExpense, "Conta de luz", 480m,
            "Utilidades", 15, new ValueObjects.YearMonth(2026, 1), null, null);

        var entry = item.GenerateEntry(new ValueObjects.YearMonth(2026, 9));

        entry.Source.Should().Be(EntrySource.Recurring);
        entry.RecurringMonth.Should().Be(new DateOnly(2026, 9, 1));

        // A conta de luz veio diferente do previsto: ajustar é o uso normal.
        entry.Invoking(e => e.EnsureEditable()).Should().NotThrow();
        entry.Update(EntryType.Expense, "Conta de luz", 512.30m, "Utilidades", Today, null);
        entry.Amount.Should().Be(512.30m);
    }

    [Fact]
    public void Deletion_is_logical()
    {
        var entry = NewEntry();

        entry.MarkAsDeleted(Now);

        entry.IsDeleted.Should().BeTrue();
        entry.DeletedAt.Should().Be(Now);
    }
}
