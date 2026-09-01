using FinFlower.Application.Common;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// Isolamento entre contas. Conhecer o id de um evento alheio não pode dar
/// nenhum acesso a ele — é a proteção contra o ataque mais comum em CRUD,
/// trocar o identificador na requisição.
/// </summary>
public class EventOwnershipTests
{
    private static readonly DateOnly EventDate = new(2026, 12, 12);

    private static CreateEventRequest NewEvent() => new("Festa da Alice", null, EventDate);

    private static CreateEntryRequest Income(decimal amount = 100m) =>
        new(EntryType.Income, "Ingressos", amount, "Vendas", EventDate);

    /// <summary>Cria um evento com um lançamento para o usuário A e passa a agir como B.</summary>
    private static async Task<(Guid EventId, Guid EntryId)> ArrangeForeignEventAsync(EventTestContext ctx)
    {
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        var entry = (await ctx.Events.AddEntryAsync(@event.Id, Income())).Value;

        ctx.ActAs();
        return (@event.Id, entry.Id);
    }

    [Fact]
    public async Task Another_users_event_is_invisible_in_the_listing()
    {
        using var ctx = new EventTestContext();
        await ArrangeForeignEventAsync(ctx);

        var list = await ctx.Events.ListAsync(new EventFilter());

        list.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Reading_another_users_event_returns_not_found()
    {
        using var ctx = new EventTestContext();
        var (eventId, _) = await ArrangeForeignEventAsync(ctx);

        var result = await ctx.Events.GetAsync(eventId);

        // 404 e não 403: confirmar que o id existe já seria informação demais.
        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Writing_to_another_users_event_returns_not_found()
    {
        using var ctx = new EventTestContext();
        var (eventId, entryId) = await ArrangeForeignEventAsync(ctx);

        var update = await ctx.Events.UpdateAsync(eventId, new UpdateEventRequest("Sequestrado", null, EventDate));
        var delete = await ctx.Events.DeleteAsync(eventId);
        var close = await ctx.Events.CloseAsync(eventId);
        var addEntry = await ctx.Events.AddEntryAsync(eventId, Income());
        var updateEntry = await ctx.Events.UpdateEntryAsync(
            eventId,
            entryId,
            new UpdateEntryRequest(EntryType.Expense, "Sequestrado", 1m, "Outros", EventDate));
        var removeEntry = await ctx.Events.RemoveEntryAsync(eventId, entryId);

        new[] { update.Error, delete.Error, close.Error, addEntry.Error, updateEntry.Error, removeEntry.Error }
            .Should().AllSatisfy(error => error!.Type.Should().Be(ErrorType.NotFound));
    }

    [Fact]
    public async Task Another_users_event_stays_untouched_after_a_failed_attempt()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var owner = ctx.CurrentUser.UserId!.Value;
        var @event = (await ctx.Events.CreateAsync(NewEvent())).Value;
        await ctx.Events.AddEntryAsync(@event.Id, Income(500m));

        ctx.ActAs();
        await ctx.Events.UpdateAsync(@event.Id, new UpdateEventRequest("Sequestrado", null, EventDate));
        await ctx.Events.DeleteAsync(@event.Id);

        ctx.ActAs(owner);
        var reloaded = (await ctx.Events.GetAsync(@event.Id)).Value;
        reloaded.Name.Should().Be("Festa da Alice");
        reloaded.TotalIncome.Should().Be(500m);
    }

    [Fact]
    public async Task Another_users_events_never_enter_the_cash_report()
    {
        using var ctx = new EventTestContext();
        await ArrangeForeignEventAsync(ctx);

        var report = (await ctx.CashReport.GetAsync(from: null, to: null)).Value;

        report.EventCount.Should().Be(0);
        report.Balance.Should().Be(0m);
        report.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_session_nothing_is_reachable()
    {
        using var ctx = new EventTestContext();
        ctx.CurrentUser.UserId = null;

        var list = await ctx.Events.ListAsync(new EventFilter());
        var create = await ctx.Events.CreateAsync(NewEvent());
        var report = await ctx.CashReport.GetAsync(null, null);

        new[] { list.Error, create.Error, report.Error }
            .Should().AllSatisfy(error => error!.Type.Should().Be(ErrorType.Unauthorized));
    }
}
