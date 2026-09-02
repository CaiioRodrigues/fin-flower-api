using FinFlower.Application.Common;
using FinFlower.Application.Contracts;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

public class ContractServiceTests
{
    private static readonly DateOnly EventDate = new(2026, 12, 12);
    private static readonly DateOnly FirstDue = new(2026, 10, 5);

    private static CreateContractRequest Receivable(decimal total = 9000m, int parts = 3) => new(
        ContractDirection.Receivable, "Prefeitura Municipal", "Show de encerramento",
        total, PaymentMethod.Boleto, parts, FirstDue, new DateOnly(2026, 9, 1));

    private static async Task<(Guid EventId, ContractResponse Contract)> ArrangeAsync(
        EventTestContext ctx,
        CreateContractRequest? request = null)
    {
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(
            new CreateEventRequest("Festa de Ano Novo", null, EventDate))).Value;

        var contract = (await ctx.Contracts.CreateAsync(@event.Id, request ?? Receivable())).Value;
        return (@event.Id, contract);
    }

    [Fact]
    public async Task Creating_a_contract_generates_the_installments()
    {
        using var ctx = new EventTestContext();

        var (_, contract) = await ArrangeAsync(ctx);

        contract.Installments.Should().HaveCount(3);
        contract.Installments.Sum(i => i.Amount).Should().Be(9000m);
        contract.OpenAmount.Should().Be(9000m);
        contract.SettledAmount.Should().Be(0m);
        contract.Installments.Should().OnlyContain(i => i.Status == InstallmentStatus.Pending);
    }

    [Fact]
    public async Task Settling_creates_the_entry_in_the_event()
    {
        using var ctx = new EventTestContext();
        var (eventId, contract) = await ArrangeAsync(ctx);

        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest());

        var @event = (await ctx.Events.GetAsync(eventId)).Value;
        @event.TotalIncome.Should().Be(3000m, "a receber liquidada vira entrada no evento");
        @event.Entries.Should().ContainSingle()
            .Which.Description.Should().Contain("parcela 1/3");
    }

    [Fact]
    public async Task Payable_contract_settles_as_an_expense()
    {
        using var ctx = new EventTestContext();
        var (eventId, contract) = await ArrangeAsync(ctx, new CreateContractRequest(
            ContractDirection.Payable, "Buffet Silva", "Jantar",
            6000m, PaymentMethod.Pix, 2, FirstDue, new DateOnly(2026, 9, 1)));

        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest());

        var @event = (await ctx.Events.GetAsync(eventId)).Value;
        @event.TotalExpense.Should().Be(3000m);
        @event.Result.Should().Be(-3000m);
    }

    [Fact]
    public async Task Settling_accepts_a_different_amount_date_and_category()
    {
        using var ctx = new EventTestContext();
        var (eventId, contract) = await ArrangeAsync(ctx);

        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest(
            SettledOn: new DateOnly(2026, 10, 3),
            Amount: 2900m,
            Category: "Patrocínio",
            Description: "Pagamento com desconto"));

        var reloaded = (await ctx.Contracts.GetAsync(contract.Id)).Value;
        var installment = reloaded.Installments.Single(i => i.Number == 1);
        installment.SettledAmount.Should().Be(2900m);
        installment.SettledOn.Should().Be(new DateOnly(2026, 10, 3));

        var entry = (await ctx.Events.GetAsync(eventId)).Value.Entries.Single();
        entry.Amount.Should().Be(2900m);
        entry.Category.Should().Be("Patrocínio");
        entry.Description.Should().Be("Pagamento com desconto");
    }

    [Fact]
    public async Task Unsettling_removes_the_entry_it_created()
    {
        using var ctx = new EventTestContext();
        var (eventId, contract) = await ArrangeAsync(ctx);
        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest());

        await ctx.Contracts.UnsettleInstallmentAsync(contract.Id, 1);

        var @event = (await ctx.Events.GetAsync(eventId)).Value;
        @event.TotalIncome.Should().Be(0m, "estornar desfaz previsto e realizado juntos");
        @event.Entries.Should().BeEmpty();

        var reloaded = (await ctx.Contracts.GetAsync(contract.Id)).Value;
        reloaded.OpenAmount.Should().Be(9000m);
    }

    [Fact]
    public async Task An_entry_that_came_from_a_contract_cannot_be_removed_by_hand()
    {
        using var ctx = new EventTestContext();
        var (eventId, contract) = await ArrangeAsync(ctx);
        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest());
        var entry = (await ctx.Events.GetAsync(eventId)).Value.Entries.Single();

        var remove = async () => await ctx.Events.RemoveEntryAsync(eventId, entry.Id);
        var update = async () => await ctx.Events.UpdateEntryAsync(
            eventId, entry.Id,
            new UpdateEntryRequest(EntryType.Income, "Adulterado", 1m, "Outros", EventDate));

        // Deixar apagar por fora quebraria o vínculo com a parcela.
        await remove.Should().ThrowAsync<DomainException>().WithMessage("*Estorne a parcela*");
        await update.Should().ThrowAsync<DomainException>().WithMessage("*Ajuste a parcela*");
    }

    [Fact]
    public async Task A_contract_with_settled_installments_cannot_be_deleted()
    {
        using var ctx = new EventTestContext();
        var (_, contract) = await ArrangeAsync(ctx);
        await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest());

        var result = await ctx.Contracts.DeleteAsync(contract.Id);

        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("contract.has_settled_installments");
    }

    [Fact]
    public async Task A_contract_without_settlements_can_be_deleted()
    {
        using var ctx = new EventTestContext();
        var (_, contract) = await ArrangeAsync(ctx);

        var result = await ctx.Contracts.DeleteAsync(contract.Id);

        result.IsSuccess.Should().BeTrue();
        (await ctx.Contracts.GetAsync(contract.Id)).Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Contracts_of_another_user_are_invisible()
    {
        using var ctx = new EventTestContext();
        var (_, contract) = await ArrangeAsync(ctx);

        ctx.ActAs();

        (await ctx.Contracts.ListAsync(new ContractFilter())).Value.Should().BeEmpty();
        (await ctx.Contracts.GetAsync(contract.Id)).Error!.Type.Should().Be(ErrorType.NotFound);
        (await ctx.Contracts.SettleInstallmentAsync(contract.Id, 1, new SettleInstallmentRequest()))
            .Error!.Type.Should().Be(ErrorType.NotFound);
        (await ctx.Contracts.DeleteAsync(contract.Id)).Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task A_contract_cannot_be_created_in_another_users_event()
    {
        using var ctx = new EventTestContext();
        var (eventId, _) = await ArrangeAsync(ctx);

        ctx.ActAs();
        var result = await ctx.Contracts.CreateAsync(eventId, Receivable());

        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Listing_can_be_filtered_by_event_direction_and_open_state()
    {
        using var ctx = new EventTestContext();
        var (eventId, receivable) = await ArrangeAsync(ctx);
        await ctx.Contracts.CreateAsync(eventId, new CreateContractRequest(
            ContractDirection.Payable, "Buffet Silva", null, 2000m,
            PaymentMethod.Pix, 1, FirstDue, new DateOnly(2026, 9, 1)));

        var payables = await ctx.Contracts.ListAsync(new ContractFilter(Direction: ContractDirection.Payable));
        var ofEvent = await ctx.Contracts.ListAsync(new ContractFilter(EventId: eventId));

        payables.Value.Should().ContainSingle().Which.Counterparty.Should().Be("Buffet Silva");
        ofEvent.Value.Should().HaveCount(2);
        ofEvent.Value.Should().Contain(c => c.Id == receivable.Id);
    }

    [Fact]
    public async Task The_summary_reports_the_next_due_date_and_the_overdue_amount()
    {
        using var ctx = new EventTestContext();
        // O relógio de teste marca 01/09/2026; a primeira parcela vence em 05/10.
        var (_, contract) = await ArrangeAsync(ctx);

        var beforeDue = (await ctx.Contracts.ListAsync(new ContractFilter())).Value.Single();
        beforeDue.NextDueDate.Should().Be(FirstDue);
        beforeDue.OverdueAmount.Should().Be(0m);

        ctx.Clock.Advance(TimeSpan.FromDays(45));
        var afterDue = (await ctx.Contracts.ListAsync(new ContractFilter())).Value.Single();

        afterDue.OverdueAmount.Should().Be(3000m, "a parcela de outubro venceu");
        contract.Installments.Should().HaveCount(3);
    }
}
