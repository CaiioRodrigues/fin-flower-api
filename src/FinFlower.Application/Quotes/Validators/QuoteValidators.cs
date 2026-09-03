using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.Entities;
using FluentValidation;

namespace FinFlower.Application.Quotes.Validators;

public sealed class CreateQuoteRequestValidator : AbstractValidator<CreateQuoteRequest>
{
    public CreateQuoteRequestValidator()
    {
        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("O cliente é obrigatório.")
            .MaximumLength(Quote.MaxClientLength);

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("O título é obrigatório.")
            .MaximumLength(Quote.MaxTitleLength);

        RuleFor(x => x.ValidUntil)
            .GreaterThanOrEqualTo(x => x.IssuedOn)
            .WithMessage("A validade não pode ser anterior à emissão.");

        RuleFor(x => x.Notes).MaximumLength(Quote.MaxNotesLength);
        RuleFor(x => x.Number).MaximumLength(Quote.MaxNumberLength);
    }
}

public sealed class UpdateQuoteRequestValidator : AbstractValidator<UpdateQuoteRequest>
{
    public UpdateQuoteRequestValidator()
    {
        RuleFor(x => x.ClientName)
            .NotEmpty().WithMessage("O cliente é obrigatório.")
            .MaximumLength(Quote.MaxClientLength);

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("O título é obrigatório.")
            .MaximumLength(Quote.MaxTitleLength);

        RuleFor(x => x.ValidUntil)
            .GreaterThanOrEqualTo(x => x.IssuedOn)
            .WithMessage("A validade não pode ser anterior à emissão.");

        RuleFor(x => x.Notes).MaximumLength(Quote.MaxNotesLength);
    }
}

public sealed class QuoteItemRequestValidator : AbstractValidator<QuoteItemRequest>
{
    public QuoteItemRequestValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A descrição do item é obrigatória.")
            .MaximumLength(QuoteItem.MaxDescriptionLength);

        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("A quantidade deve ser maior que zero.");
        RuleFor(x => x.UnitPrice).GreaterThan(0).WithMessage("O valor unitário deve ser maior que zero.");
        RuleFor(x => x.Unit).MaximumLength(QuoteItem.MaxUnitLength);
    }
}

public sealed class ApplyDiscountRequestValidator : AbstractValidator<ApplyDiscountRequest>
{
    public ApplyDiscountRequestValidator() =>
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("O desconto não pode ser negativo.");
}

public sealed class ApproveQuoteRequestValidator : AbstractValidator<ApproveQuoteRequest>
{
    public ApproveQuoteRequestValidator()
    {
        RuleFor(x => x.PaymentMethod).IsInEnum().WithMessage("Informe a forma de pagamento.");

        RuleFor(x => x.InstallmentCount)
            .InclusiveBetween(1, Contract.MaxInstallments)
            .WithMessage($"O número de parcelas deve estar entre 1 e {Contract.MaxInstallments}.");

        RuleFor(x => x.FirstDueDate)
            .NotEqual(default(DateOnly)).WithMessage("A data do primeiro vencimento é obrigatória.");

        RuleFor(x => x.Counterparty).MaximumLength(Contract.MaxCounterpartyLength);
    }
}
