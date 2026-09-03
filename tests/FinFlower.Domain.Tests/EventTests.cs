using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class EventTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static Event NewEvent() => new(Guid.CreateVersion7(), "Festa de Ano Novo", null, Today);

    [Fact]
    public void Open_event_accepts_changes()
    {
        var @event = NewEvent();

        @event.Invoking(e => e.EnsureAcceptsChanges()).Should().NotThrow();
        @event.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Closed_event_refuses_changes()
    {
        var @event = NewEvent();
        @event.Close();

        // É por aqui que a regra alcança o lançamento: ele já não vive dentro do
        // evento, mas quem for mexer nele pergunta ao evento antes.
        @event.Invoking(e => e.EnsureAcceptsChanges())
            .Should().Throw<DomainException>().WithMessage("*evento fechado*");

        @event.Invoking(e => e.UpdateDetails("Outro nome", null, Today))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Reopened_event_accepts_changes_again()
    {
        var @event = NewEvent();
        @event.Close();
        @event.Reopen();

        @event.Status.Should().Be(EventStatus.Open);
        @event.Invoking(e => e.EnsureAcceptsChanges()).Should().NotThrow();
    }

    [Fact]
    public void Closing_twice_is_rejected()
    {
        var @event = NewEvent();
        @event.Close();

        @event.Invoking(e => e.Close()).Should().Throw<DomainException>();
    }

    [Fact]
    public void Reopening_an_open_event_is_rejected()
    {
        NewEvent().Invoking(e => e.Reopen()).Should().Throw<DomainException>();
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
