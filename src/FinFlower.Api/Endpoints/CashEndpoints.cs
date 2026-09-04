using FinFlower.Api.Extensions;
using FinFlower.Application.Cash;
using FinFlower.Application.Cash.Dtos;
using FluentValidation;
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

        cash.MapGet("/opening", GetOpening)
            .WithSummary("O saldo inicial declarado, quando existe.");

        cash.MapPut("/opening", SaveOpening)
            .WithSummary("Declara quanto havia em caixa numa data — o ponto de partida do saldo.");

        cash.MapDelete("/opening", ClearOpening)
            .WithSummary("Remove o saldo inicial: o saldo volta a ser a soma dos lançamentos.");

        return app;
    }

    private static async Task<IResult> Monthly(
        IMonthlyCashService service,
        CancellationToken cancellationToken,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null) =>
        (await service.GetAsync(from, to, cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetOpening(
        ICashOpeningService service,
        CancellationToken cancellationToken) =>
        // 204 quando ninguém declarou nada. Results.Ok(null) devolveria 200 com
        // corpo vazio, e um 200 que não é JSON quebra o cliente no caso mais
        // comum de todos: o de quem ainda não usou o recurso.
        (await service.GetAsync(cancellationToken)).ToHttpResult(
            opening => opening is null ? Results.NoContent() : Results.Ok(opening));

    private static async Task<IResult> SaveOpening(
        [FromBody] SaveCashOpeningRequest request,
        ICashOpeningService service,
        IValidator<SaveCashOpeningRequest> validator,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid)
            return invalid;

        return (await service.SaveAsync(request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> ClearOpening(
        ICashOpeningService service,
        CancellationToken cancellationToken) =>
        (await service.ClearAsync(cancellationToken)).ToHttpResult();
}
