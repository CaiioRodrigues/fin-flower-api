using FinFlower.Application.Cash;
using FinFlower.Application.Contracts;
using FinFlower.Application.Entries;
using FinFlower.Application.Events;
using FinFlower.Application.Quotes;
using FinFlower.Application.Recurring;
using FinFlower.Application.Reports;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Queries;
using FinFlower.Infrastructure.Persistence.Repositories;
using FinFlower.Infrastructure.Reports;
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
        var entryRepository = new EntryRepository(Context);
        var entryQueries = new EntryQueries(Context);
        var eventRepository = new EventRepository(Context);

        Events = new EventService(
            eventRepository,
            queries,
            entryRepository,
            CurrentUser,
            Clock,
            Context);

        Entries = new EntryService(
            entryRepository,
            entryQueries,
            eventRepository,
            CurrentUser,
            Clock,
            Context);

        var contractQueries = new ContractQueries(Context);
        var recurringRepository = new RecurringItemRepository(Context);

        MonthlyCash = new MonthlyCashService(
            entryQueries,
            contractQueries,
            recurringRepository,
            CurrentUser,
            Clock);

        RecurringItems = new RecurringItemService(
            recurringRepository,
            entryRepository,
            CurrentUser,
            Clock,
            Context);

        CashReport = new CashReportService(queries, CurrentUser);

        Contracts = new ContractService(
            new ContractRepository(Context),
            contractQueries,
            eventRepository,
            entryRepository,
            CurrentUser,
            Clock,
            Context);

        Quotes = new QuoteService(
            new QuoteRepository(Context),
            new QuoteQueries(Context),
            new ContractRepository(Context),
            eventRepository,
            new UserRepository(Context),
            new QuoteProposalWriter(),
            CurrentUser,
            Clock,
            Context);

        CashFlow = new CashFlowReportService(contractQueries, CurrentUser, Clock);
    }

    public FakeClock Clock { get; }
    public FakeCurrentUser CurrentUser { get; }
    public AppDbContext Context { get; }
    public IEventService Events { get; }
    public IEntryService Entries { get; }
    public IMonthlyCashService MonthlyCash { get; }
    public IRecurringItemService RecurringItems { get; }
    public IQuoteService Quotes { get; }
    public ICashReportService CashReport { get; }
    public IContractService Contracts { get; }
    public ICashFlowReportService CashFlow { get; }

    /// <summary>Passa a agir como o usuário informado (ou um novo).</summary>
    public Guid ActAs(Guid? userId = null)
    {
        var id = userId ?? Guid.CreateVersion7();
        CurrentUser.UserId = id;
        return id;
    }

    public void Dispose() => Context.Dispose();
}
