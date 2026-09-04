using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Queries;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Application.Tests;

/// <summary>
/// As consultas de evento contra um banco relacional de verdade.
///
/// Faltava um degrau na verificação. O provedor em memória resolve as
/// subconsultas correlacionadas em LINQ-to-Objects, então ele não prova que
/// elas rodam; o teste de tradução prova que o EF gera SQL, mas não que o SQL
/// executa e devolve o número certo. Aqui os totais saem de um SELECT de
/// verdade, sobre o schema gerado pelo próprio modelo do EF.
///
/// A cobertura para aqui de propósito: o SQLite não ordena por
/// <c>DateTimeOffset</c>, e a listagem usa <c>CreatedAt</c> como critério de
/// desempate. Mudar a consulta para agradar o banco do teste seria trocar o
/// certo pelo conveniente — o SQL Server ordena isso sem problema.
/// </summary>
public class RelationalEventQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private Guid _ownerId;

    public RelationalEventQueryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options, new FakeClock(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)));
        CreateSchema();
    }

    /// <summary>
    /// O schema sai do próprio modelo do EF, com os tipos do SQL Server
    /// traduzidos: assim o teste roda contra as mesmas tabelas, colunas e
    /// índices que a migration cria, sem depender de um SQL Server.
    /// </summary>
    private void CreateSchema()
    {
        var script = _context.Database.GenerateCreateScript()
            .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
            .Replace("uniqueidentifier", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("datetimeoffset", "TEXT", StringComparison.OrdinalIgnoreCase);

        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = statement.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("GO", StringComparison.Ordinal)) continue;

            _context.Database.ExecuteSqlRaw(trimmed);
        }
    }

    /// <summary>O cenário relatado: dois eventos, um com sete lançamentos e outro com um.</summary>
    private async Task ArrangeAsync()
    {
        var user = new User("Caio", "caio@example.com", "hash");
        _ownerId = user.Id;
        _context.Users.Add(user);

        var halloween = new Event(_ownerId, "Halloween", null, new DateOnly(2026, 10, 31));
        var reveillon = new Event(_ownerId, "Reveião", null, new DateOnly(2026, 12, 31));
        _context.Events.AddRange(halloween, reveillon);

        for (var index = 0; index < 7; index++)
        {
            _context.Entries.Add(new Entry(
                _ownerId,
                index % 2 == 0 ? EntryType.Income : EntryType.Expense,
                $"Lançamento {index + 1}",
                100m * (index + 1),
                "Geral",
                new DateOnly(2026, 10, 20),
                halloween.Id));
        }

        _context.Entries.Add(new Entry(
            _ownerId, EntryType.Income, "Sinal", 5_000m, "Serviços",
            new DateOnly(2026, 12, 20), reveillon.Id));

        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task The_totals_by_event_come_out_of_real_sql()
    {
        await ArrangeAsync();
        var queries = new EventQueries(_context);

        var report = await queries.GetCashReportAsync(_ownerId, null, null);

        report.EventCount.Should().Be(2);
        report.Events.Should().HaveCount(2);

        var halloween = report.Events.Single(e => e.Name == "Halloween");
        halloween.TotalIncome.Should().Be(100m + 300m + 500m + 700m);
        halloween.TotalExpense.Should().Be(200m + 400m + 600m);
        halloween.Result.Should().Be(400m);

        report.TotalIncome.Should().Be(6_600m);
        report.Balance.Should().Be(5_400m);
    }

    [Fact]
    public async Task Another_users_events_never_come_back()
    {
        await ArrangeAsync();

        var report = await new EventQueries(_context).GetCashReportAsync(Guid.CreateVersion7(), null, null);

        // O filtro por dono está no WHERE, não numa checagem depois: contra o
        // banco de verdade isso vira um SELECT que não devolve linha nenhuma.
        report.EventCount.Should().Be(0);
        report.Balance.Should().Be(0m);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
