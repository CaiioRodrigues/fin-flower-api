using FinFlower.Api.Extensions;
using FinFlower.Application.Cash;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

/// <summary>O caixa completo, mês a mês.</summary>
public static class CashEndpoints
{
    public static IEndpointRouteBuilder MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        var cash = app.MapGroup("/api/cash")
            .WithTags("Caixa")
            .RequireAuthorization();

        cash.MapGet("/monthly", Monthly)
            .WithSummary("Entradas, saídas, resultado e saldo acumulado de cada mês do intervalo.");

        return app;
    }

    private static async Task<IResult> Monthly(
        IMonthlyCashService service,
        CancellationToken cancellationToken,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null) =>
        (await service.GetAsync(from, to, cancellationToken)).ToHttpResult();
}
