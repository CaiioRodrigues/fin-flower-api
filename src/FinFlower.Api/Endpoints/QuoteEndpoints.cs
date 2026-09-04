using FinFlower.Api.Extensions;
using FinFlower.Application.Quotes;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

/// <summary>
/// Orçamentos: a proposta montada linha a linha e, quando aceita, sua conversão
/// em contrato com parcelas.
/// </summary>
public static class QuoteEndpoints
{
    public static IEndpointRouteBuilder MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        var quotes = app.MapGroup("/api/quotes")
            .WithTags("Orçamentos")
            .RequireAuthorization();

        quotes.MapGet("/", List).WithSummary("Lista os orçamentos do usuário.");
        quotes.MapPost("/", Create).WithSummary("Abre um orçamento em rascunho.");
        quotes.MapGet("/{quoteId:guid}", Get).WithSummary("Abre um orçamento com os itens.");
        quotes.MapPut("/{quoteId:guid}", Update).WithSummary("Altera os dados do orçamento.");
        quotes.MapDelete("/{quoteId:guid}", Delete).WithSummary("Exclui o orçamento (exclusão lógica).");

        quotes.MapPost("/{quoteId:guid}/items", AddItem).WithSummary("Acrescenta uma linha ao orçamento.");
        quotes.MapPut("/{quoteId:guid}/items/{itemId:guid}", UpdateItem).WithSummary("Altera uma linha.");
        quotes.MapDelete("/{quoteId:guid}/items/{itemId:guid}", RemoveItem).WithSummary("Remove uma linha.");
        quotes.MapPut("/{quoteId:guid}/discount", ApplyDiscount).WithSummary("Aplica desconto sobre o subtotal.");

        quotes.MapPost("/{quoteId:guid}/send", Send).WithSummary("Marca como enviado ao cliente.");
        quotes.MapPost("/{quoteId:guid}/reject", Reject).WithSummary("Registra a recusa do cliente.");
        quotes.MapPost("/{quoteId:guid}/reopen", Reopen).WithSummary("Devolve um recusado a rascunho.");

        quotes.MapPost("/{quoteId:guid}/approve", Approve)
            .WithSummary("Aprova e gera o contrato com as parcelas.");

        quotes.MapGet("/{quoteId:guid}/proposal", Proposal)
            .WithSummary("Baixa a proposta comercial em PDF, pronta para enviar ao cliente.");

        return app;
    }

    private static async Task<IResult> List(
        IQuoteService service,
        CancellationToken cancellationToken,
        [FromQuery] QuoteStatus? status = null,
        [FromQuery] Guid? eventId = null,
        [FromQuery] string? search = null) =>
        (await service.ListAsync(new QuoteFilter(status, eventId, search), cancellationToken)).ToHttpResult();

    private static async Task<IResult> Get(Guid quoteId, IQuoteService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(quoteId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Create(
        [FromBody] CreateQuoteRequest request,
        IValidator<CreateQuoteRequest> validator,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await service.CreateAsync(request, cancellationToken);
        return result.ToHttpResult(response => Results.Created($"/api/quotes/{response.Id}", response));
    }

    private static async Task<IResult> Update(
        Guid quoteId,
        [FromBody] UpdateQuoteRequest request,
        IValidator<UpdateQuoteRequest> validator,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.UpdateAsync(quoteId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Delete(
        Guid quoteId,
        IQuoteService service,
        CancellationToken cancellationToken) =>
        (await service.DeleteAsync(quoteId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> AddItem(
        Guid quoteId,
        [FromBody] QuoteItemRequest request,
        IValidator<QuoteItemRequest> validator,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.AddItemAsync(quoteId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> UpdateItem(
        Guid quoteId,
        Guid itemId,
        [FromBody] QuoteItemRequest request,
        IValidator<QuoteItemRequest> validator,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.UpdateItemAsync(quoteId, itemId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> RemoveItem(
        Guid quoteId,
        Guid itemId,
        IQuoteService service,
        CancellationToken cancellationToken) =>
        (await service.RemoveItemAsync(quoteId, itemId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> ApplyDiscount(
        Guid quoteId,
        [FromBody] ApplyDiscountRequest request,
        IValidator<ApplyDiscountRequest> validator,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.ApplyDiscountAsync(quoteId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Send(
        Guid quoteId,
        IQuoteService service,
        CancellationToken cancellationToken) =>
        (await service.SendAsync(quoteId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Reject(
        Guid quoteId,
        IQuoteService service,
        CancellationToken cancellationToken) =>
        (await service.RejectAsync(quoteId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Reopen(
        Guid quoteId,
        IQuoteService service,
        CancellationToken cancellationToken) =>
        (await service.ReopenAsync(quoteId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Proposal(
        Guid quoteId,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ExportProposalAsync(quoteId, cancellationToken);

        return result.ToHttpResult(file =>
            Results.File(file.Content, file.ContentType, file.FileName));
    }

    private static async Task<IResult> Approve(
        Guid quoteId,
        [FromBody] ApproveQuoteRequest request,
        IValidator<ApproveQuoteRequest> validator,
        IQuoteService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.ApproveAsync(quoteId, request, cancellationToken)).ToHttpResult();
    }
}
