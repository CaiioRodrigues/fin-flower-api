using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class ContractTests
{
    private static readonly DateOnly Signed = new(2026, 9, 1);
    private static readonly DateOnly FirstDue = new(2026, 10, 5);

    private static Contract NewContract(decimal total = 9000m, int installments = 3) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        ContractDirection.Receivable,
        "Prefeitura Municipal",
        "Show de encerramento",
        total,
        PaymentMethod.Boleto,
        installments,
        FirstDue,
        Signed);

    [Fact]
    public void Installments_are_generated_with_sequential_numbers_and_monthly_due_dates()
    {
        var contract = NewContract();

        contract.Installments.Select(i => i.Number).Should().Equal(1, 2, 3);
        contract.Installments.Select(i => i.DueDate).Should().Equal(
            new DateOnly(2026, 10, 5),
            new DateOnly(2026, 11, 5),
            new DateOnly(2026, 12, 5));
    }

    [Fact]
    public void Installments_always_add_up_to_the_contract_total()
    {
        // 1000 em 3 daria 333,33 três vezes e fecharia em 999,99.
        var contract = NewContract(total: 1000m, installments: 3);

        contract.Installments.Select(i => i.Amount).Should().Equal(333.33m, 333.33m, 333.34m);
        contract.Installments.Sum(i => i.Amount).Should().Be(1000m);
    }

    [Theory]
    [InlineData(100, 3)]
    [InlineData(0.03, 3)]
    [InlineData(1234.56, 7)]
    [InlineData(999.99, 12)]
    [InlineData(50000, 24)]
    public void No_cent_is_lost_in_any_split(decimal total, int parts)
    {
        var contract = NewContract(total, parts);

        contract.Installments.Sum(i => i.Amount).Should().Be(total);
        contract.Installments.Should().OnlyContain(i => i.Amount > 0);
    }

    [Fact]
    public void End_of_month_due_dates_do_not_overflow()
    {
        var contract = new Contract(
            Guid.CreateVersion7(), Guid.CreateVersion7(), ContractDirection.Receivable,
            "Cliente", null, 300m, PaymentMethod.Pix, 3,
            firstDueDate: new DateOnly(2026, 1, 31), signedOn: Signed);

        contract.Installments.Select(i => i.DueDate).Should().Equal(
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 28),
            new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void A_value_too_small_to_split_is_rejected()
    {
        var act = () => NewContract(total: 0.02m, installments: 3);

        act.Should().Throw<DomainException>().WithMessage("*baixo demais*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Contract.MaxInstallments + 1)]
    public void Installment_count_outside_the_limits_is_rejected(int count)
    {
        var act = () => NewContract(installments: count);

        act.Should().Throw<DomainException>().WithMessage("*parcelas*");
    }

    [Fact]
    public void Settling_records_the_amount_the_date_and_the_entry()
    {
        var contract = NewContract();
        var entryId = Guid.CreateVersion7();

        contract.SettleInstallment(1, new DateOnly(2026, 10, 3), 2900m, entryId);

        var installment = contract.FindInstallment(1);
        installment.Status.Should().Be(InstallmentStatus.Settled);
        installment.SettledAmount.Should().Be(2900m, "o cliente pagou com desconto");
        installment.EntryId.Should().Be(entryId);
        contract.SettledAmount.Should().Be(2900m);
        contract.OpenAmount.Should().Be(6000m);
    }

    [Fact]
    public void Settling_twice_is_rejected()
    {
        var contract = NewContract();
        contract.SettleInstallment(1, FirstDue, 3000m, Guid.CreateVersion7());

        contract.Invoking(c => c.SettleInstallment(1, FirstDue, 3000m, Guid.CreateVersion7()))
            .Should().Throw<DomainException>().WithMessage("*já foi liquidada*");
    }

    [Fact]
    public void Unsettling_returns_the_entry_and_reopens_the_installment()
    {
        var contract = NewContract();
        var entryId = Guid.CreateVersion7();
        contract.SettleInstallment(2, FirstDue, 3000m, entryId);

        var removed = contract.UnsettleInstallment(2);

        removed.Should().Be(entryId, "o caso de uso precisa saber qual lançamento apagar");
        contract.FindInstallment(2).Status.Should().Be(InstallmentStatus.Pending);
        contract.SettledAmount.Should().Be(0m);
    }

    [Fact]
    public void Overdue_is_read_from_the_date_not_stored()
    {
        var contract = NewContract();

        contract.FindInstallment(1).IsOverdue(new DateOnly(2026, 10, 4)).Should().BeFalse();
        contract.FindInstallment(1).IsOverdue(new DateOnly(2026, 10, 6)).Should().BeTrue();

        contract.SettleInstallment(1, new DateOnly(2026, 10, 6), 3000m, Guid.CreateVersion7());
        contract.FindInstallment(1).IsOverdue(new DateOnly(2026, 12, 1))
            .Should().BeFalse("parcela liquidada não fica vencida");
    }

    [Fact]
    public void Canceled_installment_leaves_the_forecast()
    {
        var contract = NewContract();

        contract.CancelInstallment(3);

        contract.ActiveAmount.Should().Be(6000m);
        contract.OpenAmount.Should().Be(6000m);
    }

    [Fact]
    public void A_settled_installment_cannot_be_canceled()
    {
        var contract = NewContract();
        contract.SettleInstallment(1, FirstDue, 3000m, Guid.CreateVersion7());

        contract.Invoking(c => c.CancelInstallment(1))
            .Should().Throw<DomainException>().WithMessage("*Estorne antes*");
    }

    [Fact]
    public void Changing_one_installment_redistributes_the_difference()
    {
        var contract = NewContract();

        contract.ChangeInstallmentAmount(1, 5000m);

        contract.FindInstallment(1).Amount.Should().Be(5000m);
        contract.Installments.Sum(i => i.Amount).Should().Be(9000m, "o total contratado não muda");
        contract.FindInstallment(2).Amount.Should().Be(2000m);
        contract.FindInstallment(3).Amount.Should().Be(2000m);
    }

    [Fact]
    public void Changing_an_installment_beyond_the_total_is_rejected()
    {
        var contract = NewContract();

        contract.Invoking(c => c.ChangeInstallmentAmount(1, 9000m))
            .Should().Throw<DomainException>().WithMessage("*sem saldo*");
    }

    [Fact]
    public void An_unknown_installment_is_rejected()
    {
        var contract = NewContract();

        contract.Invoking(c => c.FindInstallment(99))
            .Should().Throw<DomainException>().WithMessage("*não encontrada*");
    }
}
