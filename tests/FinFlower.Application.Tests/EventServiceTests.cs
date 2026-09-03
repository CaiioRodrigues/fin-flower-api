using FinFlower.Application.Common;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

public class EventServiceTests
{
    private static readonly DateOnly EventDate = new(2026, 12, 12);

    private static CreateEventRequest NewEvent(string name = "Festa de Ano Novo") =>
        new(name, "Réveillon na praia", EventDate);

    private static CreateEntryRequest Income(decimal amount, Guid eventId, string description = "Ingressos") =>
        new(EntryType.Income, description, amount, "Vendas", EventDate, eventId);

    private static CreateEntryRequest Expense(decimal amount, Guid eventId, string description = "Buffet") =>
        new(EntryType.Expense, description, amount, "Alimentação", EventDate, eventId);

    [Fact]
    public async Task Create_returns_the_event_with_zeroed_totals()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var result = await ctx.Events.CreateAsync(NewEvent());

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Festa de Ano Novo");
        result.Value.Status.Should().Be(EventStatus.Open);
        result.Value.Result.Should().Be(0m);
        result.Value.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Entries_feed_the_event_totals()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;

        await ctx.Entries.CreateAsync(Income(8000m, @event.Id));
        await ctx.Entries.CreateAsync(Expense(3000m, @event.Id, "Aluguel do espaço"));
        await ctx.Entries.CreateAsync(Expense(2500m, @event.Id));

        var reloaded = (await ctx.Events.GetAsync(@event.Id)).Value;

        reloaded.TotalIncome.Should().Be(8000m);
        reloaded.TotalExpense.Should().Be(5500m);
        reloaded.Result.Should().Be(2500m);
        reloaded.IsProfitable.Should().BeTrue();
        reloaded.Entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task Removed_entry_leaves_the_totals_and_the_listing()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        var wrong = (await ctx.Entries.CreateAsync(Expense(500m, @event.Id, "Lançado errado"))).Value;
        await ctx.Entries.CreateAsync(Expense(200m, @event.Id));

        await ctx.Entries.DeleteAsync(wrong.Id);

        var reloaded = (await ctx.Events.GetAsync(@event.Id)).Value;
        reloaded.TotalExpense.Should().Be(200m);
        reloaded.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Entry_can_be_corrected()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        var entry = (await ctx.Entries.CreateAsync(Income(100m, @event.Id))).Value;

        await ctx.Entries.UpdateAsync(
            entry.Id,
            new UpdateEntryRequest(EntryType.Expense, "Reembolso", 30m, "Outros", EventDate, @event.Id));

        var reloaded = (await ctx.Events.GetAsync(@event.Id)).Value;
        reloaded.TotalIncome.Should().Be(0m);
        reloaded.TotalExpense.Should().Be(30m);
        reloaded.Result.Should().Be(-30m);
    }

    [Fact]
    public async Task Closed_event_refuses_new_entries()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        await ctx.Events.CloseAsync(@event.Id);

        var act = async () => await ctx.Entries.CreateAsync(Income(100m, @event.Id));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*evento fechado*");
    }

    [Fact]
    public async Task Reopened_event_accepts_entries_again()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        await ctx.Events.CloseAsync(@event.Id);

        await ctx.Events.ReopenAsync(@event.Id);
        var added = await ctx.Entries.CreateAsync(Income(100m, @event.Id));

        added.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Deleted_event_disappears_from_the_listing()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;

        await ctx.Events.DeleteAsync(@event.Id);

        (await ctx.Events.ListAsync(new EventFilter())).Value.Should().BeEmpty();
        (await ctx.Events.GetAsync(@event.Id)).Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Event_with_entries_refuses_to_be_deleted()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        await ctx.Entries.CreateAsync(Income(100m, @event.Id));

        var result = await ctx.Events.DeleteAsync(@event.Id);

        // O lançamento é do caixa: apagar o evento não pode fazer o dinheiro
        // sumir nem deixá-lo apontando para um evento que já não existe.
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error!.Code.Should().Be("event.has_entries");
    }

    [Fact]
    public async Task Listing_can_be_filtered_by_period_and_status()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var january = (await ctx.Events.CreateAsync(
            new CreateEventRequest("Evento de janeiro", null, new DateOnly(2026, 1, 15)))).Value;
        await ctx.Events.CreateAsync(new CreateEventRequest("Evento de junho", null, new DateOnly(2026, 6, 10)));
        await ctx.Events.CloseAsync(january.Id);

        var firstQuarter = await ctx.Events.ListAsync(
            new EventFilter(From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 3, 31)));
        var closed = await ctx.Events.ListAsync(new EventFilter(Status: EventStatus.Closed));

        firstQuarter.Value.Should().ContainSingle().Which.Name.Should().Be("Evento de janeiro");
        closed.Value.Should().ContainSingle().Which.Name.Should().Be("Evento de janeiro");
    }

    [Fact]
    public async Task Listing_is_ordered_from_the_most_recent_event()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await ctx.Events.CreateAsync(new CreateEventRequest("Antigo", null, new DateOnly(2026, 1, 1)));
        await ctx.Events.CreateAsync(new CreateEventRequest("Recente", null, new DateOnly(2026, 9, 1)));

        var list = (await ctx.Events.ListAsync(new EventFilter())).Value;

        list.Select(e => e.Name).Should().ContainInOrder("Recente", "Antigo");
    }
}
