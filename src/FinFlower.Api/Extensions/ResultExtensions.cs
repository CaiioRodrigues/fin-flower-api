using FinFlower.Application.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FinFlower.Api.Extensions;

/// <summary>
/// Único ponto de tradução entre erro de negócio e status HTTP. Os endpoints
/// não repetem esse mapeamento, e ele não escapa para a camada de aplicação.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)
            : Problem(result.Error!);

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    private static IResult Problem(Error error)
    {
        var (statusCode, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Requisição inválida"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflito"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Não autenticado"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Acesso negado"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno"),
        };

        return Results.Problem(
            title: title,
            detail: error.Message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    /// <summary>Erros de validação no formato padrão do ASP.NET (RFC 7807 + campo).</summary>
    public static ValidationProblem ToValidationProblem(this FluentValidation.Results.ValidationResult result) =>
        TypedResults.ValidationProblem(
            result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            title: "Requisição inválida");
}
