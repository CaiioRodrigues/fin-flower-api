using FinFlower.Application.Auth.Dtos;
using FinFlower.Domain.Entities;
using FluentValidation;

namespace FinFlower.Application.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Informe o nome.")
            .MaximumLength(User.MaxNameLength);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Informe o e-mail.")
            .EmailAddress().WithMessage("Informe um e-mail válido.")
            .MaximumLength(User.MaxEmailLength);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Informe a senha.")
            .MinimumLength(8).WithMessage("A senha deve ter ao menos 8 caracteres.")
            .MaximumLength(128).WithMessage("A senha deve ter no máximo 128 caracteres.")
            .Matches("[A-Z]").WithMessage("A senha deve conter ao menos uma letra maiúscula.")
            .Matches("[a-z]").WithMessage("A senha deve conter ao menos uma letra minúscula.")
            .Matches("[0-9]").WithMessage("A senha deve conter ao menos um número.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Informe o e-mail.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Informe a senha.");
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Informe o refresh token.");
}
