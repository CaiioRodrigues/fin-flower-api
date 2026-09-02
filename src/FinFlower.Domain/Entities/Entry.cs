using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Um lançamento dentro de um evento: uma entrada (receita) ou uma saída (despesa).
/// Só é criado e alterado através do <see cref="Event"/> que o contém, para que
/// as regras de evento fechado nunca possam ser contornadas.
/// </summary>
public sealed class Entry : AuditableEntity
{
    public const int MaxDescriptionLength = 200;
    public const int MaxCategoryLength = 60;

    private Entry() { } // EF Core

    internal Entry(
        Guid eventId,
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn,
        Guid? installmentId = null)
    {
        InstallmentId = installmentId;
        EventId = eventId;
        Type = type;
        Description = Guard.AgainstNullOrWhiteSpace(description, "descrição", MaxDescriptionLength);
        Amount = Guard.AgainstNonPositiveMoney(amount, "valor");
        Category = Guard.AgainstNullOrWhiteSpace(category, "categoria", MaxCategoryLength);
        OccurredOn = occurredOn;
    }

    public Guid EventId { get; private set; }
    public EntryType Type { get; private set; }
    public string Description { get; private set; } = null!;

    /// <summary>Sempre positivo. O sentido é dado por <see cref="Type"/>.</summary>
    public decimal Amount { get; private set; }

    public string Category { get; private set; } = null!;
    public DateOnly OccurredOn { get; private set; }

    /// <summary>
    /// Parcela que originou este lançamento, quando ele veio de um contrato.
    /// Enquanto existir, o lançamento não pode ser removido nem alterado por
    /// fora: quem manda é a parcela, e estornar lá desfaz os dois juntos.
    /// </summary>
    public Guid? InstallmentId { get; private set; }

    public bool ComesFromContract => InstallmentId is not null;

    /// <summary>Valor com sinal, para somatórios: receita positiva, despesa negativa.</summary>
    public decimal SignedAmount => Type == EntryType.Income ? Amount : -Amount;

    internal void Update(
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn)
    {
        Type = type;
        Description = Guard.AgainstNullOrWhiteSpace(description, "descrição", MaxDescriptionLength);
        Amount = Guard.AgainstNonPositiveMoney(amount, "valor");
        Category = Guard.AgainstNullOrWhiteSpace(category, "categoria", MaxCategoryLength);
        OccurredOn = occurredOn;
    }
}
