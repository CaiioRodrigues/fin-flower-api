using FinFlower.Application.Cash.Dtos;
using FinFlower.Domain.Entities;
using FluentValidation;

namespace FinFlower.Application.Cash.Validators;

public sealed class SaveCashOpeningRequestValidator : AbstractValidator<SaveCashOpeningRequest>
{
    /// <summary>
    /// Um teto largo, só para barrar dedo escorregado na tecla: o campo aceita
    /// negativo e zero de propósito, então não há regra de sinal para aplicar.
    /// </summary>
    private const decimal Limit = 1_000_000_000m;

    public SaveCashOpeningRequestValidator()
    {
        RuleFor(x => x.Amount)
            .InclusiveBetween(-Limit, Limit)
            .WithMessage("O saldo inicial está fora de qualquer valor plausível.");

        RuleFor(x => x.OccurredOn)
            .Must(date => date >= new DateOnly(2000, 1, 1))
            .WithMessage("A data do saldo inicial parece errada — informe o dia em que você conferiu o saldo.");

        RuleFor(x => x.Notes)
            .MaximumLength(CashOpening.MaxNotesLength);
    }
}
