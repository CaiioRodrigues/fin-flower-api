using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.ValueObjects;
using FluentValidation;

namespace FinFlower.Application.Recurring.Validators;

public interface IRecurringItemFields
{
    string Description { get; }
    decimal Amount { get; }
    string Category { get; }
    int DayOfMonth { get; }
    string? EndMonth { get; }
    string? Notes { get; }
}

public abstract class RecurringItemFieldsValidator<T> : AbstractValidator<T> where T : IRecurringItemFields
{
    protected RecurringItemFieldsValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A descrição é obrigatória.")
            .MaximumLength(RecurringItem.MaxDescriptionLength);

        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("O valor deve ser maior que zero.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("A categoria é obrigatória.")
            .MaximumLength(RecurringItem.MaxCategoryLength);

        RuleFor(x => x.DayOfMonth)
            .InclusiveBetween(1, 31).WithMessage("O dia do vencimento deve estar entre 1 e 31.");

        RuleFor(x => x.EndMonth)
            .Must(BeACompetence).WithMessage("O mês final deve estar no formato aaaa-mm.")
            .When(x => !string.IsNullOrWhiteSpace(x.EndMonth));

        RuleFor(x => x.Notes).MaximumLength(RecurringItem.MaxNotesLength);
    }

    protected static bool BeACompetence(string? value) => YearMonth.TryParse(value, out _);
}

public sealed class CreateRecurringItemRequestValidator : RecurringItemFieldsValidator<CreateRecurringItemRequest>
{
    public CreateRecurringItemRequestValidator()
    {
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Informe o tipo do item fixo.");

        RuleFor(x => x.StartMonth)
            .NotEmpty().WithMessage("O mês inicial é obrigatório.")
            .Must(BeACompetence).WithMessage("O mês inicial deve estar no formato aaaa-mm.");
    }
}

public sealed class UpdateRecurringItemRequestValidator : RecurringItemFieldsValidator<UpdateRecurringItemRequest>;
