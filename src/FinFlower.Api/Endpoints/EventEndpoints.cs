using FinFlower.Api.Extensions;
using FinFlower.Application.Events;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // Autorização no grupo inteiro: um endpoint novo já nasce protegido,
        // em vez de depender de alguém lembrar de anotá-lo.
        var events = app.MapGroup("/api/events")
            .WithTags("Eventos")
            .RequireAuthorization();

        events.MapGet("/", List).WithSummary("Lista os eventos do usuário com os totais de cada um.");
        events.MapPost("/", Create).WithSummary("Cria um evento.");
        events.MapGet("/{eventId:guid}", Get).WithSummary("Abre um evento com todos os seus lançamentos.");
        events.MapPut("/{eventId:guid}", Update).WithSummary("Altera os dados do evento.");
        events.MapDelete("/{eventId:guid}", Delete).WithSummary("Exclui o evento (exclusão lógica).");
        events.MapPost("/{eventId:guid}/close", Close).WithSummary("Fecha o evento e congela o resultado.");
        events.MapPost("/{eventId:guid}/reopen", Reopen).WithSummary("Reabre um evento fechado.");

        // Os lançamentos do evento são criados e alterados pelo livro-caixa,
        // em /api/entries com o eventId no corpo: o lançamento é do caixa, e o
        // evento é um atributo dele.
        return app;
    }

    private static async Task<IResult> List(
        IEventService service,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] EventStatus? status = null)
    {
        var result = await service.ListAsync(new EventFilter(from, to, status), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Get(Guid eventId, IEventService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(eventId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Create(
        [FromBody] CreateEventRequest request,
        IValidator<CreateEventRequest> validator,
        IEventService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await service.CreateAsync(request, cancellationToken);
        return result.ToHttpResult(response => Results.Created($"/api/events/{response.Id}", response));
    }

    private static async Task<IResult> Update(
        Guid eventId,
        [FromBody] UpdateEventRequest request,
        IValidator<UpdateEventRequest> validator,
        IEventService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.UpdateAsync(eventId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Delete(Guid eventId, IEventService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(eventId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Close(Guid eventId, IEventService service, CancellationToken cancellationToken) =>
        (await service.CloseAsync(eventId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Reopen(Guid eventId, IEventService service, CancellationToken cancellationToken) =>
        (await service.ReopenAsync(eventId, cancellationToken)).ToHttpResult();
}
