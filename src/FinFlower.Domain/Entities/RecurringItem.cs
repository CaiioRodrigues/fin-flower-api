using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Um compromisso que se repete todo mês — gasto fixo, pró-labore ou receita
/// recorrente. Não é um lançamento: é a regra que produz um lançamento por
/// competência, e por isso alterar o valor aqui não reescreve o passado, só
/// muda o que ainda vai ser gerado.
/// </summary>
public sealed class RecurringItem : AuditableEntity
{
    public const int MaxDescriptionLength = 200;
    public const int MaxCategoryLength = 60;
    public const int MaxNotesLength = 500;

    private RecurringItem() { } // EF Core

    public RecurringItem(
        Guid ownerId,
        RecurringKind kind,
        string description,
        decimal amount,
        string category,
        int dayOfMonth,
        YearMonth startMonth,
        YearMonth? endMonth,
        string? notes)
    {
        if (ownerId == Guid.Empty) throw new DomainException("O item fixo precisa de um dono.");

        OwnerId = ownerId;
        Kind = kind;
        IsActive = true;
        StartMonth = startMonth.FirstDay;

        Apply(description, amount, category, dayOfMonth, endMonth, notes);
    }

    public Guid OwnerId { get; private set; }
    public RecurringKind Kind { get; private set; }
    public string Description { get; private set; } = null!;

    /// <summary>Valor previsto por mês. Sempre positivo — o sentido vem de <see cref="Kind"/>.</summary>
    public decimal Amount { get; private set; }

    public string Category { get; private set; } = null!;

    /// <summary>Dia do vencimento. Meses curtos usam o último dia, via <see cref="YearMonth.DayOrLast"/>.</summary>
    public int DayOfMonth { get; private set; }

    /// <summary>Primeira competência, gravada como o primeiro dia do mês.</summary>
    public DateOnly StartMonth { get; private set; }

    /// <summary>Última competência, quando o compromisso tem fim.</summary>
    public DateOnly? EndMonth { get; private set; }

    /// <summary>Inativo para de gerar, mas o que já foi gerado continua no caixa.</summary>
    public bool IsActive { get; private set; }

    public string? Notes { get; private set; }

    public YearMonth Start => YearMonth.From(StartMonth);
    public YearMonth? End => EndMonth is { } end ? YearMonth.From(end) : null;

    /// <summary>Pró-labore e gasto fixo saem do caixa; receita recorrente entra.</summary>
    public EntryType EntryType => Kind == RecurringKind.FixedIncome ? EntryType.Income : EntryType.Expense;

    /// <summary>Se esta competência está dentro da vigência de um item ativo.</summary>
    public bool IsDueIn(YearMonth competence) =>
        IsActive
        && competence >= Start
        && (End is not { } end || competence <= end);

    public DateOnly DueDateIn(YearMonth competence) => competence.DayOrLast(DayOfMonth);

    public void UpdateDetails(
        string description,
        decimal amount,
        string category,
        int dayOfMonth,
        YearMonth? endMonth,
        string? notes) =>
        Apply(description, amount, category, dayOfMonth, endMonth, notes);

    public void Activate()
    {
        if (IsActive) throw new DomainException("Este item já está ativo.");
        IsActive = true;
    }

    public void Deactivate()
    {
        if (!IsActive) throw new DomainException("Este item já está inativo.");
        IsActive = false;
    }

    /// <summary>
    /// Materializa a competência como lançamento. Quem chama garante que o mês
    /// ainda não foi gerado — a unicidade de (item, competência) está no banco.
    /// </summary>
    public Entry GenerateEntry(YearMonth competence)
    {
        if (!IsDueIn(competence))
        {
            throw new DomainException(
                $"O item '{Description}' não vale para a competência {competence}.");
        }

        return Entry.FromRecurringItem(
            OwnerId,
            Id,
            competence,
            EntryType,
            Description,
            Amount,
            Category,
            DueDateIn(competence));
    }

    private void Apply(
        string description,
        decimal amount,
        string category,
        int dayOfMonth,
        YearMonth? endMonth,
        string? notes)
    {
        if (dayOfMonth is < 1 or > 31)
            throw new DomainException("O dia do vencimento deve estar entre 1 e 31.");

        if (endMonth is { } end && end < Start)
            throw new DomainException("O mês final não pode ser anterior ao inicial.");

        Description = Guard.AgainstNullOrWhiteSpace(description, "descrição", MaxDescriptionLength);
        Amount = Guard.AgainstNonPositiveMoney(amount, "valor");
        Category = Guard.AgainstNullOrWhiteSpace(category, "categoria", MaxCategoryLength);
        DayOfMonth = dayOfMonth;
        EndMonth = endMonth?.FirstDay;
        Notes = NormalizeNotes(notes);
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;

        var trimmed = notes.Trim();
        if (trimmed.Length > MaxNotesLength)
            throw new DomainException($"As observações devem ter no máximo {MaxNotesLength} caracteres.");

        return trimmed;
    }
}
