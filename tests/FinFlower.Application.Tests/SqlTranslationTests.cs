using FinFlower.Application.Events.Dtos;
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
