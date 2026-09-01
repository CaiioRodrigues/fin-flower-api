using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Entities;
using FluentValidation;

namespace FinFlower.Application.Events.Validators;

/// <summary>
/// Regras compartilhadas por criação e edição. A validação aqui é a primeira
/// barreira; o domínio revalida, porque ele não confia em quem o chama.
/// </summary>
internal static class EventRules
{
    public static IRuleBuilderOptions<T, string> EventName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Informe o nome do evento.")
            .MaximumLength(Event.MaxNameLength)
            .WithMessage($"O nome deve ter no máximo {Event.MaxNameLength} caracteres.");

    public static IRuleBuilderOptions<T, string?> EventDescription<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(Event.MaxDescriptionLength)
            .WithMessage($"A descrição deve ter no máximo {Event.MaxDescriptionLength} caracteres.");

    public static void EntryFields<T>(AbstractValidator<T> validator)
        where T : IEntryFields
    {
        validator.RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Tipo inválido. Use 'Income' (entrada) ou 'Expense' (saída).");

        validator.RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Informe a descrição do lançamento.")
            .MaximumLength(Entry.MaxDescriptionLength)
            .WithMessage($"A descrição deve ter no máximo {Entry.MaxDescriptionLength} caracteres.");

        validator.RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("O valor deve ser maior que zero.")
            .LessThanOrEqualTo(999_999_999.99m).WithMessage("O valor informado é alto demais.");

        validator.RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Informe a categoria.")
            .MaximumLength(Entry.MaxCategoryLength)
            .WithMessage($"A categoria deve ter no máximo {Entry.MaxCategoryLength} caracteres.");
    }
}

/// <summary>Campos comuns de criação e edição de lançamento.</summary>
public interface IEntryFields
{
    Domain.Enums.EntryType Type { get; }
    string Description { get; }
    decimal Amount { get; }
    string Category { get; }
}

public sealed class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.Name).EventName();
        RuleFor(x => x.Description).EventDescription();
    }
}

public sealed class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        RuleFor(x => x.Name).EventName();
        RuleFor(x => x.Description).EventDescription();
    }
}

public sealed class CreateEntryRequestValidator : AbstractValidator<CreateEntryRequest>
{
    public CreateEntryRequestValidator() => EventRules.EntryFields(this);
}

public sealed class UpdateEntryRequestValidator : AbstractValidator<UpdateEntryRequest>
{
    public UpdateEntryRequestValidator() => EventRules.EntryFields(this);
}
