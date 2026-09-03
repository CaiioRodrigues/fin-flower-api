using FinFlower.Application.Contracts;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.ValueObjects;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Application.Tests;

/// <summary>
/// O provedor em memória aceita qualquer LINQ, então ele não prova que as
/// consultas rodam no SQL Server. Aqui elas são montadas contra o provedor real
/// (sem banco atrás) só para verificar que o EF consegue traduzi-las para SQL —
/// uma projeção não traduzível quebraria apenas em produção.
/// </summary>
public class SqlTranslationTests
{
    private static AppDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=nao-conecta;Database=FinFlower;User Id=sa;Password=x;TrustServerCertificate=True;Connect Timeout=1")
            .Options;

        return new AppDbContext(options, new FakeClock(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Every_read_query_translates_to_sql_server()
    {
        using var context = SqlServerContext();
        var queries = new EventQueries(context);
        var ownerId = Guid.CreateVersion7();

        // Sem banco atrás, a falha esperada é de conexão. Se a projeção não fosse
        // traduzível, o EF lançaria antes disso, com "could not be translated".
        var calls = new Func<Task>[]
        {
            () => queries.ListAsync(ownerId, new EventFilter()),
            () => queries.ListAsync(ownerId, new EventFilter(
                From: new DateOnly(2026, 1, 1),
                To: new DateOnly(2026, 12, 31),
                Status: Domain.Enums.EventStatus.Closed)),
            () => queries.GetDetailsAsync(Guid.CreateVersion7(), ownerId),
            () => queries.GetCashReportAsync(ownerId, null, null),
        };

        await AssertTranslatesAsync(calls);
    }

    [Fact]
    public async Task Every_ledger_and_cash_query_translates_to_sql_server()
    {
        using var context = SqlServerContext();
        var queries = new EntryQueries(context);
        var ownerId = Guid.CreateVersion7();

        var calls = new Func<Task>[]
        {
            () => queries.ListAsync(ownerId, new EntryFilter(), 1, 50),
            () => queries.ListAsync(ownerId, new EntryFilter(
                From: new DateOnly(2026, 1, 1),
                To: new DateOnly(2026, 12, 31),
                Type: Domain.Enums.EntryType.Expense,
                Source: Domain.Enums.EntrySource.Recurring,
                EventId: Guid.CreateVersion7(),
                Category: "Estrutura",
                Search: "aluguel"), 2, 25),
            () => queries.ListAsync(ownerId, new EntryFilter(WithoutEvent: true), 1, 50),
            () => queries.GetAsync(Guid.CreateVersion7(), ownerId),

            // A mais arriscada do sistema: agrupamento com junção à esquerda no
            // item fixo, para separar pró-labore de gasto fixo dentro do mês.
            () => queries.GetMonthlyBucketsAsync(ownerId, new YearMonth(2026, 1), new YearMonth(2026, 12)),
            () => queries.GetBalanceBeforeAsync(ownerId, new YearMonth(2026, 1)),
            () => queries.GetGeneratedRecurringMonthsAsync(ownerId, new YearMonth(2026, 1), new YearMonth(2026, 12)),
            () => queries.ListCategoriesAsync(ownerId),
        };

        await AssertTranslatesAsync(calls);
    }

    [Fact]
    public async Task Every_quote_and_contract_query_translates_to_sql_server()
    {
        using var context = SqlServerContext();
        var quotes = new QuoteQueries(context);
        var contracts = new ContractQueries(context);
        var ownerId = Guid.CreateVersion7();
        var today = new DateOnly(2026, 9, 1);

        var calls = new Func<Task>[]
        {
            () => quotes.ListAsync(ownerId, new QuoteFilter(), today),
            () => quotes.ListAsync(ownerId, new QuoteFilter(
                Status: Domain.Enums.QuoteStatus.Sent,
                EventId: Guid.CreateVersion7(),
                Search: "prefeitura"), today),
            () => quotes.GetAsync(Guid.CreateVersion7(), ownerId, today),
            () => contracts.ListAsync(ownerId, new ContractFilter(), today),
            () => contracts.GetAsync(Guid.CreateVersion7(), ownerId, today),
            () => contracts.GetCashFlowAsync(ownerId, today, 6),

            // O previsto do caixa: agrupamento de parcelas em aberto por mês de
            // vencimento, com o sentido vindo de uma subconsulta no contrato.
            () => contracts.GetInstallmentForecastAsync(
                ownerId, new YearMonth(2026, 1), new YearMonth(2026, 12), today),
            () => contracts.GetOverdueTotalsAsync(ownerId, today),
        };

        await AssertTranslatesAsync(calls);
    }

    private static async Task AssertTranslatesAsync(Func<Task>[] calls)
    {

        foreach (var call in calls)
        {
            var thrown = await Record.ExceptionAsync(call);

            thrown.Should().NotBeNull("a consulta chega ao banco inexistente");
            thrown!.ToString().Should().NotContain(
                "could not be translated",
                "toda projeção precisa virar SQL de verdade");
        }
    }
}
