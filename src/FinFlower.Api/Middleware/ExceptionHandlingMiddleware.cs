using FinFlower.Domain.Common;

namespace FinFlower.Api.Middleware;

/// <summary>
/// Rede de proteção final. Converte exceção de domínio em 400 e qualquer outra
/// em 500 genérico — mensagem de exceção interna nunca chega ao cliente, porque
/// stack trace e detalhe de banco são informação útil para um atacante.
/// </summary>
public sealed partial class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            LogDomainViolation(logger, context.Request.Path, ex);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Requisição inválida", ex.Message);
        }
        catch (Exception ex)
        {
            LogUnhandledError(logger, context.Request.Path, ex);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Erro interno",
                "Ocorreu um erro inesperado. Tente novamente mais tarde.");
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Regra de negócio violada em {Path}")]
    private static partial void LogDomainViolation(ILogger logger, PathString path, Exception exception);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Erro não tratado em {Path}")]
    private static partial void LogUnhandledError(ILogger logger, PathString path, Exception exception);

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        if (context.Response.HasStarted) return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://httpstatuses.io/{statusCode}",
            title,
            status = statusCode,
            detail,
            traceId = context.TraceIdentifier,
        });
    }
}
