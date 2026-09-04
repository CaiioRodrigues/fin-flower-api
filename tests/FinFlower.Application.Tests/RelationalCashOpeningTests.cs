using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Queries;
using FinFlower.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Application.Tests;

/// <summary>
/// O saldo inicial contra um banco relacional de verdade.
///
/// Dois motivos para não parar no provedor em memória. O corte por data vira um
/// WHERE que precisa executar e devolver o número certo, não só compilar. E o
/// índice único do dono é a única coisa que impede dois saldos iniciais de se
/// somarem em silêncio — em memória ele nem existe, então lá a garantia é
/// imaginária.
/// </summary>
public class RelationalCashOpeningTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly Guid _ownerId;

    public RelationalCashOpeningTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options, new FakeClock(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)));

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

        var user = new User("Caio", "caio@example.com", "hash");
        _ownerId = user.Id;
        _context.Users.Add(user);
        _context.SaveChanges();
    }

    private void AddEntry(EntryType type, decimal amount, DateOnly on) =>
        _context.Entries.Add(new Entry(_ownerId, type, "Lançamento", amount, "Geral", on));

    [Fact]
    public async Task The_cutoff_runs_in_sql_and_returns_the_right_number()
    {
        AddEntry(EntryType.Income, 8_000m, new DateOnly(2026, 7, 15));
        AddEntry(EntryType.Expense, 200m, new DateOnly(2026, 8, 2));
        AddEntry(EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));
        await _context.SaveChangesAsync();

        var queries = new EntryQueries(_context);
        var cutoff = new DateOnly(2026, 9, 1);

        var buckets = await queries.GetMonthlyBucketsAsync(
            _ownerId, new YearMonth(2026, 7), new YearMonth(2026, 9), cutoff);

        buckets.Sum(b => b.Amount).Should().Be(1_000m, "só setembro entra na conta");
        buckets.Should().OnlyContain(b => b.Month == 9);

        (await queries.CountBeforeAsync(_ownerId, cutoff)).Should().Be(2);

        // A janela de outubro: o que veio antes do corte não pode ser somado.
        var before = await queries.GetBalanceBeforeAsync(_ownerId, new YearMonth(2026, 10), cutoff);
        before.Should().Be(1_000m);
    }

    [Fact]
    public async Task The_database_refuses_a_second_opening_for_the_same_owner()
    {
        _context.CashOpenings.Add(new CashOpening(_ownerId, 30_000m, new DateOnly(2026, 9, 1)));
        await _context.SaveChangesAsync();

        _context.CashOpenings.Add(new CashOpening(_ownerId, 5_000m, new DateOnly(2026, 9, 2)));

        // Dois saldos iniciais se somariam sem nenhum sintoma na tela. O índice
        // único é o que garante que isso não passe nem por um caminho de código
        // que ninguém previu.
        var save = async () => await _context.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
