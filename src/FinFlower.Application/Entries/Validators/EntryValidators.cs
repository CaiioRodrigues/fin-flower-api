using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentValidation;

namespace FinFlower.Application.Entries.Validators;

/// <summary>
/// Os campos comuns a criar e alterar. A regra mora num lugar só, e as duas
/// requisições não podem divergir por esquecimento.
/// </summary>
public interface IEntryFields
{
    EntryType Type { get; }
    string Description { get; }
    decimal Amount { get; }
    string Category { get; }
    DateOnly OccurredOn { get; }
}

public abstract class EntryFieldsValidator<T> : AbstractValidator<T> where T : IEntryFields
{
    /// <summary>Teto de sanidade: acima disto é dedo escorregado, não dinheiro.</summary>
    public const decimal MaxAmount = 999_999_999.99m;

    protected EntryFieldsValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Informe se é entrada ou saída.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A descrição é obrigatória.")
            .MaximumLength(Entry.MaxDescriptionLength);

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("O valor deve ser maior que zero.")
            .LessThanOrEqualTo(MaxAmount).WithMessage("O valor informado é alto demais.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("A categoria é obrigatória.")
            .MaximumLength(Entry.MaxCategoryLength);

        RuleFor(x => x.OccurredOn)
            .NotEqual(default(DateOnly)).WithMessage("A data é obrigatória.");
    }
}

public sealed class CreateEntryRequestValidator : EntryFieldsValidator<CreateEntryRequest>;

public sealed class UpdateEntryRequestValidator : EntryFieldsValidator<UpdateEntryRequest>;
