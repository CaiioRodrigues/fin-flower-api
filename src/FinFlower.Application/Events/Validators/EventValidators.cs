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
