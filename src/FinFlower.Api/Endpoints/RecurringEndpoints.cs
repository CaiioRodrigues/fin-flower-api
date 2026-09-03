using FinFlower.Api.Extensions;
using FinFlower.Application.Recurring;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

/// <summary>
/// Gastos fixos e pró-labore. As duas telas consomem os mesmos endpoints,
/// separadas pelo filtro <c>kind</c>.
/// </summary>
public static class RecurringEndpoints
{
    public static IEndpointRouteBuilder MapRecurringEndpoints(this IEndpointRouteBuilder app)
    {
        var items = app.MapGroup("/api/recurring-items")
            .WithTags("Fixos e pró-labore")
            .RequireAuthorization();

        items.MapGet("/", List).WithSummary("Itens fixos com a situação da competência pedida.");
        items.MapPost("/", Create).WithSummary("Cadastra um gasto fixo, pró-labore ou receita recorrente.");
        items.MapPut("/{itemId:guid}", Update).WithSummary("Altera o item — vale para os meses ainda não gerados.");
        items.MapPost("/{itemId:guid}/activate", Activate).WithSummary("Reativa um item.");
        items.MapPost("/{itemId:guid}/deactivate", Deactivate).WithSummary("Suspende um item sem apagar o histórico.");
        items.MapDelete("/{itemId:guid}", Delete).WithSummary("Exclui o item (exclusão lógica).");

        items.MapPost("/generate", Generate)
            .WithSummary("Lança no caixa os itens da competência. Rodar duas vezes não duplica.");

        return app;
    }

    private static async Task<IResult> List(
        IRecurringItemService service,
        CancellationToken cancellationToken,
        [FromQuery] RecurringKind? kind = null,
        [FromQuery] bool? onlyActive = null,
        [FromQuery] string? competence = null) =>
        (await service.ListAsync(new RecurringFilter(kind, onlyActive), competence, cancellationToken))
        .ToHttpResult();

    private static async Task<IResult> Create(
        [FromBody] CreateRecurringItemRequest request,
        IValidator<CreateRecurringItemRequest> validator,
        IRecurringItemService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await service.CreateAsync(request, cancellationToken);
        return result.ToHttpResult(response => Results.Created($"/api/recurring-items/{response.Id}", response));
    }

    private static async Task<IResult> Update(
        Guid itemId,
        [FromBody] UpdateRecurringItemRequest request,
        IValidator<UpdateRecurringItemRequest> validator,
        IRecurringItemService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.UpdateAsync(itemId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Activate(
        Guid itemId,
        IRecurringItemService service,
        CancellationToken cancellationToken) =>
        (await service.SetActiveAsync(itemId, active: true, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Deactivate(
        Guid itemId,
        IRecurringItemService service,
        CancellationToken cancellationToken) =>
        (await service.SetActiveAsync(itemId, active: false, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Delete(
        Guid itemId,
        IRecurringItemService service,
        CancellationToken cancellationToken) =>
        (await service.DeleteAsync(itemId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Generate(
        [FromBody] GenerateMonthRequest? request,
        IRecurringItemService service,
        CancellationToken cancellationToken) =>
        (await service.GenerateMonthAsync(request?.Competence, request?.ItemIds, cancellationToken))
        .ToHttpResult();
}

/// <summary>
/// Competência em branco significa o mês corrente; lista de itens em branco,
/// todos os que valem para o mês.
/// </summary>
public sealed record GenerateMonthRequest(string? Competence = null, IReadOnlyList<Guid>? ItemIds = null);
