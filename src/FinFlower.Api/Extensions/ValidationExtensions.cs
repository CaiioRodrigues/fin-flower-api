using FluentValidation;

namespace FinFlower.Api.Extensions;

public static class ValidationExtensions
{
    /// <summary>
    /// Valida o corpo antes de qualquer regra de negócio. Retorna null quando
    /// está tudo certo, ou a resposta 400 já formatada.
    /// </summary>
    public static async Task<IResult?> ValidateRequestAsync<T>(
        this IValidator<T> validator,
        T request,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.IsValid ? null : result.ToValidationProblem();
    }
}
