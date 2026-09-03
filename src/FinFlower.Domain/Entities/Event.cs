using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Um evento: o rótulo que agrupa lançamentos para apurar resultado por trabalho
/// realizado. Não é mais o dono dos lançamentos — o livro-caixa é — mas continua
/// dizendo quando para de aceitar movimentação, através de <see cref="EnsureAcceptsChanges"/>.
/// </summary>
public sealed class Event : AuditableEntity
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;

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

    public bool IsClosed => Status == EventStatus.Closed;

    public void UpdateDetails(string name, string? description, DateOnly eventDate)
    {
        EnsureAcceptsChanges();
        Name = Guard.AgainstNullOrWhiteSpace(name, "nome", MaxNameLength);
        Description = NormalizeDescription(description);
        EventDate = eventDate;
    }

    /// <summary>
    /// A regra do evento fechado, num lugar só. Quem cria, altera ou remove um
    /// lançamento ligado a um evento pergunta aqui antes — a regra continua sendo
    /// do domínio, ainda que o lançamento já não viva dentro do agregado.
    /// </summary>
    public void EnsureAcceptsChanges()
    {
        if (IsClosed)
            throw new DomainException("Não é possível alterar um evento fechado. Reabra o evento primeiro.");
    }

    public void Close()
    {
        if (IsClosed) throw new DomainException("Este evento já está fechado.");
        Status = EventStatus.Closed;
    }

    public void Reopen()
    {
        if (!IsClosed) throw new DomainException("Este evento já está aberto.");
        Status = EventStatus.Open;
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
