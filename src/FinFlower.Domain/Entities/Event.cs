using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Um evento e seus lançamentos. É a raiz de agregação: toda criação, alteração
/// e remoção de lançamento passa por aqui, então "evento fechado não muda" vale
/// sempre, venha a chamada de onde vier.
/// </summary>
public sealed class Event : AuditableEntity
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;

    private readonly List<Entry> _entries = [];

    private Event() { } // EF Core

    public Event(Guid ownerId, string name, string? description, DateOnly eventDate)
    {
        if (ownerId == Guid.Empty)
            throw new DomainException("O evento precisa de um dono.");

        OwnerId = ownerId;
        Name = Guard.AgainstNullOrWhiteSpace(name, "nome", MaxNameLength);
        Description = NormalizeDescription(description);
        EventDate = eventDate;
        Status = EventStatus.Open;
    }

    /// <summary>Dono do evento. Toda consulta é filtrada por ele — é o que impede
    /// um usuário de ler ou alterar dados de outro.</summary>
    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateOnly EventDate { get; private set; }
    public EventStatus Status { get; private set; }

    public IReadOnlyCollection<Entry> Entries => _entries.AsReadOnly();

    public decimal TotalIncome => _entries
        .Where(e => !e.IsDeleted && e.Type == EntryType.Income)
        .Sum(e => e.Amount);

    public decimal TotalExpense => _entries
        .Where(e => !e.IsDeleted && e.Type == EntryType.Expense)
        .Sum(e => e.Amount);

    /// <summary>Entradas menos saídas. Positivo é lucro, negativo é prejuízo.</summary>
    public decimal Result => TotalIncome - TotalExpense;

    public bool IsProfitable => Result > 0;

    public void UpdateDetails(string name, string? description, DateOnly eventDate)
    {
        EnsureOpen();
        Name = Guard.AgainstNullOrWhiteSpace(name, "nome", MaxNameLength);
        Description = NormalizeDescription(description);
        EventDate = eventDate;
    }

    public Entry AddEntry(
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn)
    {
        EnsureOpen();
        var entry = new Entry(Id, type, description, amount, category, occurredOn);
        _entries.Add(entry);
        return entry;
    }

    public void UpdateEntry(
        Guid entryId,
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn)
    {
        EnsureOpen();
        FindEntry(entryId).Update(type, description, amount, category, occurredOn);
    }

    public void RemoveEntry(Guid entryId, DateTimeOffset now)
    {
        EnsureOpen();
        FindEntry(entryId).MarkAsDeleted(now);
    }

    public void Close()
    {
        if (Status == EventStatus.Closed)
            throw new DomainException("Este evento já está fechado.");

        Status = EventStatus.Closed;
    }

    public void Reopen()
    {
        if (Status == EventStatus.Open)
            throw new DomainException("Este evento já está aberto.");

        Status = EventStatus.Open;
    }

    private Entry FindEntry(Guid entryId)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == entryId && !e.IsDeleted)
            ?? throw new DomainException("Lançamento não encontrado neste evento.");

        return entry;
    }

    private void EnsureOpen()
    {
        if (Status == EventStatus.Closed)
            throw new DomainException("Não é possível alterar um evento fechado. Reabra o evento primeiro.");
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var trimmed = description.Trim();
        if (trimmed.Length > MaxDescriptionLength)
            throw new DomainException($"O campo 'descrição' deve ter no máximo {MaxDescriptionLength} caracteres.");

        return trimmed;
    }
}
