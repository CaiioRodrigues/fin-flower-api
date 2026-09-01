using FinFlower.Application.Events;
using FinFlower.Application.Reports;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Queries;
using FinFlower.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Application.Tests;

/// <summary>
/// Monta os serviços de evento e caixa com repositório e consultas reais sobre
/// um banco em memória. <see cref="ActAs"/> troca o usuário da sessão, que é o
/// que permite testar o isolamento entre contas.
/// </summary>
public sealed class EventTestContext : IDisposable
{
    public EventTestContext()
    {
        Clock = new FakeClock(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        CurrentUser = new FakeCurrentUser();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"event-tests-{Guid.CreateVersion7()}")
            .Options;

        Context = new AppDbContext(options, Clock);

        var queries = new EventQueries(Context);

        Events = new EventService(
            new EventRepository(Context),
            queries,
            CurrentUser,
            Clock,
            Context);

        CashReport = new CashReportService(queries, CurrentUser);
    }

    public FakeClock Clock { get; }
    public FakeCurrentUser CurrentUser { get; }
    public AppDbContext Context { get; }
    public IEventService Events { get; }
    public ICashReportService CashReport { get; }

    /// <summary>Passa a agir como o usuário informado (ou um novo).</summary>
    public Guid ActAs(Guid? userId = null)
    {
        var id = userId ?? Guid.CreateVersion7();
        CurrentUser.UserId = id;
        return id;
    }

    public void Dispose() => Context.Dispose();
}
