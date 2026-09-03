using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Uma movimentação de dinheiro: entrou ou saiu, num dia, com um valor.
///
/// É a raiz do sistema. O evento é um atributo opcional — serve para agrupar e
/// apurar resultado por evento — mas o caixa existe sem ele: aluguel, pró-labore
/// e gasto fixo são lançamentos sem evento nenhum.
/// </summary>
public sealed class Entry : AuditableEntity
{
    public const int MaxDescriptionLength = 200;
    public const int MaxCategoryLength = 60;

    private Entry() { } // EF Core

    public Entry(
        Guid ownerId,
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn,
        Guid? eventId = null)
    {
        if (ownerId == Guid.Empty) throw new DomainException("O lançamento precisa de um dono.");

        OwnerId = ownerId;
        EventId = eventId;
        Source = EntrySource.Manual;
        Type = type;
        Description = Guard.AgainstNullOrWhiteSpace(description, "descrição", MaxDescriptionLength);
        Amount = Guard.AgainstNonPositiveMoney(amount, "valor");
        Category = Guard.AgainstNullOrWhiteSpace(category, "categoria", MaxCategoryLength);
        OccurredOn = occurredOn;
    }

    /// <summary>Dono do lançamento. Toda consulta filtra por ele — é o que impede
    /// um usuário de ler ou alterar dado de outro.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Evento a que o lançamento pertence, quando pertence a algum.</summary>
    public Guid? EventId { get; private set; }

    public EntrySource Source { get; private set; }
    public EntryType Type { get; private set; }
    public string Description { get; private set; } = null!;

    /// <summary>Sempre positivo. O sentido é dado por <see cref="Type"/>.</summary>
    public decimal Amount { get; private set; }

    public string Category { get; private set; } = null!;

    /// <summary>Dia em que o dinheiro se moveu. É por ele que o lançamento cai num mês.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>
    /// Parcela que originou este lançamento. Enquanto existir, quem manda é a
    /// parcela: estornar lá desfaz os dois juntos.
    /// </summary>
    public Guid? InstallmentId { get; private set; }

    /// <summary>Item fixo que originou este lançamento, com a competência em
    /// <see cref="RecurringMonth"/>. O par é único: gerar o mesmo mês duas vezes
    /// não duplica a despesa.</summary>
    public Guid? RecurringItemId { get; private set; }

    /// <summary>Competência do item fixo, gravada como o primeiro dia do mês.</summary>
    public DateOnly? RecurringMonth { get; private set; }

    public bool ComesFromContract => Source == EntrySource.Contract;
    public bool ComesFromRecurringItem => Source == EntrySource.Recurring;

    /// <summary>A competência em que o lançamento entra no caixa.</summary>
    public YearMonth Competence => YearMonth.From(OccurredOn);

    /// <summary>Valor com sinal, para somatórios: receita positiva, despesa negativa.</summary>
    public decimal SignedAmount => Type == EntryType.Income ? Amount : -Amount;

    /// <summary>Lançamento nascido da liquidação de uma parcela.</summary>
    internal static Entry FromInstallment(
        Guid ownerId,
        Guid installmentId,
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn,
        Guid? eventId)
    {
        var entry = new Entry(ownerId, type, description, amount, category, occurredOn, eventId)
        {
            Source = EntrySource.Contract,
            InstallmentId = installmentId,
        };

        return entry;
    }

    /// <summary>Lançamento nascido da competência de um item fixo.</summary>
    internal static Entry FromRecurringItem(
        Guid ownerId,
        Guid recurringItemId,
        YearMonth competence,
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn)
    {
        var entry = new Entry(ownerId, type, description, amount, category, occurredOn)
        {
            Source = EntrySource.Recurring,
            RecurringItemId = recurringItemId,
            RecurringMonth = competence.FirstDay,
        };

        return entry;
    }

    public void Update(
        EntryType type,
        string description,
        decimal amount,
        string category,
        DateOnly occurredOn,
        Guid? eventId)
    {
        EnsureEditable();

        Type = type;
        Description = Guard.AgainstNullOrWhiteSpace(description, "descrição", MaxDescriptionLength);
        Amount = Guard.AgainstNonPositiveMoney(amount, "valor");
        Category = Guard.AgainstNullOrWhiteSpace(category, "categoria", MaxCategoryLength);
        OccurredOn = occurredOn;
        EventId = eventId;
    }

    /// <summary>Alteração feita pela parcela dona do lançamento, na liquidação.</summary>
    internal void UpdateFromInstallment(
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

    /// <summary>
    /// O que veio de contrato pertence à parcela; o que veio de item fixo é só
    /// uma previsão materializada, e ajustá-la é o uso normal.
    ///
    /// A mensagem muda com a intenção porque a saída é outra: para corrigir o
    /// valor, mexe-se na parcela; para tirar do caixa, estorna-se.
    /// </summary>
    public void EnsureEditable()
    {
        if (ComesFromContract)
        {
            throw new DomainException(
                "Este lançamento veio de uma parcela de contrato. Ajuste a parcela para alterá-lo.");
        }
    }

    public void EnsureRemovable()
    {
        if (ComesFromContract)
        {
            throw new DomainException(
                "Este lançamento veio de uma parcela de contrato. Estorne a parcela para removê-lo.");
        }
    }
}
