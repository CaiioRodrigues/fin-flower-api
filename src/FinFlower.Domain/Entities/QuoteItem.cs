using FinFlower.Domain.Common;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Uma linha do orçamento: o que é, quanto, por quanto cada. Só nasce e muda
/// através do <see cref="Quote"/>, para o total nunca divergir da soma das linhas.
/// </summary>
public sealed class QuoteItem : Entity
{
    public const int MaxDescriptionLength = 200;
    public const int MaxUnitLength = 20;

    private QuoteItem() { } // EF Core

    internal QuoteItem(Guid quoteId, int position, string description, decimal quantity, decimal unitPrice, string? unit)
    {
        QuoteId = quoteId;
        Position = position;
        Apply(description, quantity, unitPrice, unit);
    }

    public Guid QuoteId { get; private set; }

    /// <summary>Ordem em que a linha aparece no orçamento.</summary>
    public int Position { get; private set; }

    public string Description { get; private set; } = null!;

    /// <summary>Aceita fração — 2,5 diárias, 1,5 hora.</summary>
    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    /// <summary>Unidade livre: "un", "h", "diária", "m²".</summary>
    public string? Unit { get; private set; }

    /// <summary>Arredondado a duas casas aqui, não no somatório: o cliente confere
    /// linha a linha, e a soma tem de bater com o que ele lê.</summary>
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    internal void Update(string description, decimal quantity, decimal unitPrice, string? unit) =>
        Apply(description, quantity, unitPrice, unit);

    internal void MoveTo(int position) => Position = position;

    private void Apply(string description, decimal quantity, decimal unitPrice, string? unit)
    {
        if (quantity <= 0) throw new DomainException("A quantidade deve ser maior que zero.");
        if (unitPrice <= 0) throw new DomainException("O valor unitário deve ser maior que zero.");

        Description = Guard.AgainstNullOrWhiteSpace(description, "descrição do item", MaxDescriptionLength);
        Quantity = decimal.Round(quantity, 3, MidpointRounding.AwayFromZero);
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);

        Unit = string.IsNullOrWhiteSpace(unit)
            ? null
            : Guard.AgainstNullOrWhiteSpace(unit, "unidade", MaxUnitLength);
    }
}
