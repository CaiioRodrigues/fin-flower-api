using FinFlower.Api.Extensions;
using FinFlower.Application.Entries;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

/// <summary>
/// O livro-caixa: entrada e saída de dinheiro. O evento virou um filtro entre
/// outros — passar <c>eventId</c> traz o extrato dele, e <c>semEvento=true</c>
/// traz o que não pertence a trabalho nenhum.
/// </summary>
public static class EntryEndpoints
{
    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder app)
    {
        // Autorização no grupo inteiro: um endpoint novo já nasce protegido,
        // em vez de depender de alguém lembrar de anotá-lo.
        var entries = app.MapGroup("/api/entries")
            .WithTags("Lançamentos")
            .RequireAuthorization();

        entries.MapGet("/", List).WithSummary("Lista o livro-caixa, com filtros e totais do período.");
        entries.MapGet("/categories", ListCategories).WithSummary("Categorias já usadas, para sugerir no formulário.");
        entries.MapGet("/{entryId:guid}", Get).WithSummary("Abre um lançamento.");
        entries.MapPost("/", Create).WithSummary("Registra uma entrada ou saída.");
        entries.MapPut("/{entryId:guid}", Update).WithSummary("Altera um lançamento.");
        entries.MapDelete("/{entryId:guid}", Delete).WithSummary("Remove um lançamento (exclusão lógica).");

        return app;
    }

    private static async Task<IResult> List(
        IEntryService service,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] EntryType? type = null,
        [FromQuery] EntrySource? source = null,
        [FromQuery] Guid? eventId = null,
        [FromQuery] bool? withoutEvent = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = EntryService.DefaultPageSize)
    {
        var filter = new EntryFilter(from, to, type, source, eventId, withoutEvent, category, search);
        return (await service.ListAsync(filter, page, pageSize, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> ListCategories(IEntryService service, CancellationToken cancellationToken) =>
        (await service.ListCategoriesAsync(cancellationToken)).ToHttpResult();

    private static async Task<IResult> Get(Guid entryId, IEntryService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(entryId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Create(
        [FromBody] CreateEntryRequest request,
        IValidator<CreateEntryRequest> validator,
        IEntryService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await service.CreateAsync(request, cancellationToken);
        return result.ToHttpResult(response => Results.Created($"/api/entries/{response.Id}", response));
    }

    private static async Task<IResult> Update(
        Guid entryId,
        [FromBody] UpdateEntryRequest request,
        IValidator<UpdateEntryRequest> validator,
        IEntryService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.UpdateAsync(entryId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Delete(
        Guid entryId,
        IEntryService service,
        CancellationToken cancellationToken) =>
        (await service.DeleteAsync(entryId, cancellationToken)).ToHttpResult();
}
