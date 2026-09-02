using FinFlower.Application.Contracts.Dtos;
using FinFlower.Domain.Entities;
using FluentValidation;

namespace FinFlower.Application.Contracts.Validators;

public sealed class CreateContractRequestValidator : AbstractValidator<CreateContractRequest>
{
    public CreateContractRequestValidator()
    {
        RuleFor(x => x.Direction).IsInEnum()
            .WithMessage("Sentido inválido. Use 'Receivable' (a receber) ou 'Payable' (a pagar).");

        RuleFor(x => x.Counterparty)
            .NotEmpty().WithMessage("Informe o contratante.")
            .MaximumLength(Contract.MaxCounterpartyLength);

        RuleFor(x => x.Description).MaximumLength(Contract.MaxDescriptionLength);

        RuleFor(x => x.PaymentMethod).IsInEnum().WithMessage("Forma de pagamento inválida.");

        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("O valor total deve ser maior que zero.")
            .LessThanOrEqualTo(999_999_999.99m).WithMessage("O valor informado é alto demais.");

        RuleFor(x => x.InstallmentCount)
            .InclusiveBetween(1, Contract.MaxInstallments)
            .WithMessage($"O número de parcelas deve estar entre 1 e {Contract.MaxInstallments}.");
    }
}

public sealed class UpdateContractRequestValidator : AbstractValidator<UpdateContractRequest>
{
    public UpdateContractRequestValidator()
    {
        RuleFor(x => x.Direction).IsInEnum().WithMessage("Sentido inválido.");
        RuleFor(x => x.Counterparty)
            .NotEmpty().WithMessage("Informe o contratante.")
            .MaximumLength(Contract.MaxCounterpartyLength);
        RuleFor(x => x.Description).MaximumLength(Contract.MaxDescriptionLength);
        RuleFor(x => x.PaymentMethod).IsInEnum().WithMessage("Forma de pagamento inválida.");
    }
}

public sealed class SettleInstallmentRequestValidator : AbstractValidator<SettleInstallmentRequest>
{
    public SettleInstallmentRequestValidator()
    {
        // Todos opcionais: em branco valem o valor e a data da própria parcela.
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("O valor liquidado deve ser maior que zero.")
            .When(x => x.Amount is not null);

        RuleFor(x => x.Category).MaximumLength(Entry.MaxCategoryLength).When(x => x.Category is not null);
        RuleFor(x => x.Description).MaximumLength(Entry.MaxDescriptionLength).When(x => x.Description is not null);
    }
}
