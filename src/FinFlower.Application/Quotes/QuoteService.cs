using System.Globalization;
using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Quotes;

public interface IQuoteService
{
    Task<Result<IReadOnlyList<QuoteSummaryResponse>>> ListAsync(QuoteFilter filter, CancellationToken ct = default);
    Task<Result<QuoteResponse>> GetAsync(Guid quoteId, CancellationToken ct = default);
    Task<Result<QuoteResponse>> CreateAsync(CreateQuoteRequest request, CancellationToken ct = default);
    Task<Result<QuoteResponse>> UpdateAsync(Guid quoteId, UpdateQuoteRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid quoteId, CancellationToken ct = default);

    Task<Result<QuoteResponse>> AddItemAsync(Guid quoteId, QuoteItemRequest request, CancellationToken ct = default);
    Task<Result<QuoteResponse>> UpdateItemAsync(Guid quoteId, Guid itemId, QuoteItemRequest request, CancellationToken ct = default);
    Task<Result<QuoteResponse>> RemoveItemAsync(Guid quoteId, Guid itemId, CancellationToken ct = default);
    Task<Result<QuoteResponse>> ApplyDiscountAsync(Guid quoteId, ApplyDiscountRequest request, CancellationToken ct = default);

    Task<Result<QuoteResponse>> SendAsync(Guid quoteId, CancellationToken ct = default);
    Task<Result<QuoteResponse>> RejectAsync(Guid quoteId, CancellationToken ct = default);
    Task<Result<QuoteResponse>> ReopenAsync(Guid quoteId, CancellationToken ct = default);
    Task<Result<QuoteResponse>> ApproveAsync(Guid quoteId, ApproveQuoteRequest request, CancellationToken ct = default);

    /// <summary>A proposta impressa, pronta para enviar ao cliente.</summary>
    Task<Result<Reports.Export.ReportFile>> ExportProposalAsync(Guid quoteId, CancellationToken ct = default);
}

/// <summary>
/// Orçamentos: a proposta montada linha a linha e, quando aceita, sua conversão
/// em contrato. A aprovação é o único ponto do sistema em que uma venda vira
/// previsão de caixa, e por isso ela é atômica — orçamento aprovado e contrato
/// com parcelas gravam na mesma transação, ou nenhum dos dois grava.
/// </summary>
public sealed class QuoteService(
    IQuoteRepository quotes,
    IQuoteQueries queries,
    IContractRepository contracts,
    IEventRepository events,
    IUserRepository users,
    IQuoteProposalWriter proposals,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IQuoteService
{
    private const string NumberPrefix = "ORC";

    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    private static Error QuoteNotFound() =>
        Error.NotFound("quote.not_found", "Orçamento não encontrado.");

    public async Task<Result<IReadOnlyList<QuoteSummaryResponse>>> ListAsync(
        QuoteFilter filter,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<IReadOnlyList<QuoteSummaryResponse>>(NoSession);

        return Result.Success(await queries.ListAsync(ownerId, filter, Today, ct));
    }

    public async Task<Result<QuoteResponse>> GetAsync(Guid quoteId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<QuoteResponse>(NoSession);

        return await ReadAsync(quoteId, ownerId, ct);
    }

    public async Task<Result<QuoteResponse>> CreateAsync(
        CreateQuoteRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<QuoteResponse>(NoSession);

        var linked = await EnsureEventExistsAsync(request.EventId, ownerId, ct);
        if (linked.IsFailure) return Result.Failure<QuoteResponse>(linked.Error!);

        var number = await ResolveNumberAsync(ownerId, request.Number, ct);
        if (number.IsFailure) return Result.Failure<QuoteResponse>(number.Error!);

        var quote = new Quote(
            ownerId,
            number.Value,
            request.ClientName,
            request.Title,
            request.IssuedOn,
            request.ValidUntil,
            request.Notes,
            request.EventId);

        quotes.Add(quote);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(quote.Id, ownerId, ct);
    }

    public async Task<Result<QuoteResponse>> UpdateAsync(
        Guid quoteId,
        UpdateQuoteRequest request,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(quoteId, ct);
        if (loaded.IsFailure) return Result.Failure<QuoteResponse>(loaded.Error!);

        var linked = await EnsureEventExistsAsync(request.EventId, loaded.Value.OwnerId, ct);
        if (linked.IsFailure) return Result.Failure<QuoteResponse>(linked.Error!);

        loaded.Value.UpdateDetails(
            request.ClientName,
            request.Title,
            request.IssuedOn,
            request.ValidUntil,
            request.Notes);

        loaded.Value.AttachToEvent(request.EventId);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(quoteId, loaded.Value.OwnerId, ct);
    }

    public async Task<Result> DeleteAsync(Guid quoteId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(quoteId, ct);
        if (loaded.IsFailure) return Result.Failure(loaded.Error!);

        // Um orçamento aprovado já virou contrato: apagá-lo deixaria o contrato
        // sem a proposta que o originou.
        if (loaded.Value.ContractId is not null)
        {
            return Result.Failure(Error.Conflict(
                "quote.already_approved",
                "Este orçamento já virou contrato. Exclua o contrato antes."));
        }

        loaded.Value.MarkAsDeleted(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public Task<Result<QuoteResponse>> AddItemAsync(
        Guid quoteId,
        QuoteItemRequest request,
        CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.AddItem(
            request.Description, request.Quantity, request.UnitPrice, request.Unit), ct);

    public Task<Result<QuoteResponse>> UpdateItemAsync(
        Guid quoteId,
        Guid itemId,
        QuoteItemRequest request,
        CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.UpdateItem(
            itemId, request.Description, request.Quantity, request.UnitPrice, request.Unit), ct);

    public Task<Result<QuoteResponse>> RemoveItemAsync(Guid quoteId, Guid itemId, CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.RemoveItem(itemId), ct);

    public Task<Result<QuoteResponse>> ApplyDiscountAsync(
        Guid quoteId,
        ApplyDiscountRequest request,
        CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.ApplyDiscount(request.Amount), ct);

    public Task<Result<QuoteResponse>> SendAsync(Guid quoteId, CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.MarkAsSent(), ct);

    public Task<Result<QuoteResponse>> RejectAsync(Guid quoteId, CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.Reject(), ct);

    public Task<Result<QuoteResponse>> ReopenAsync(Guid quoteId, CancellationToken ct = default) =>
        MutateAsync(quoteId, quote => quote.Reopen(), ct);

    public async Task<Result<QuoteResponse>> ApproveAsync(
        Guid quoteId,
        ApproveQuoteRequest request,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(quoteId, ct);
        if (loaded.IsFailure) return Result.Failure<QuoteResponse>(loaded.Error!);

        var quote = loaded.Value;

        // O evento pode ter sido fechado entre a proposta e o aceite: um contrato
        // novo em evento fechado geraria lançamentos que o evento não aceita.
        if (quote.EventId is { } eventId)
        {
            var @event = await events.GetByIdAsync(eventId, quote.OwnerId, ct);
            if (@event is null)
                return Result.Failure<QuoteResponse>(Error.NotFound("event.not_found", "Evento não encontrado."));

            @event.EnsureAcceptsChanges();
        }

        // A checagem vem antes de montar o contrato: um orçamento vazio deve
        // falhar dizendo "sem itens", e não com a mensagem do total do contrato.
        quote.EnsureCanBeApproved();

        var contract = new Contract(
            quote.OwnerId,
            Domain.Enums.ContractDirection.Receivable,
            request.Counterparty ?? quote.ClientName,
            $"Orçamento {quote.Number} — {quote.Title}",
            quote.Total,
            request.PaymentMethod,
            request.InstallmentCount,
            request.FirstDueDate,
            request.SignedOn,
            quote.EventId,
            quote.Id);

        // Orçamento e contrato gravam na mesma transação: ou os dois existem,
        // ou nenhum deles.
        quote.Approve(contract.Id);

        contracts.Add(contract);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(quoteId, quote.OwnerId, ct);
    }

    public async Task<Result<Reports.Export.ReportFile>> ExportProposalAsync(
        Guid quoteId,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<Reports.Export.ReportFile>(NoSession);

        var quote = await queries.GetAsync(quoteId, ownerId, Today, ct);
        if (quote is null) return Result.Failure<Reports.Export.ReportFile>(QuoteNotFound());

        // O emissor da proposta é quem está logado: é o nome que o cliente vê
        // no papel timbrado. Sem ele o documento sairia anônimo.
        var issuer = await users.GetByIdAsync(ownerId, ct);
        if (issuer is null)
            return Result.Failure<Reports.Export.ReportFile>(NoSession);

        var proposal = new QuoteProposal(
            quote.Number,
            issuer.Name,
            issuer.Email,
            quote.ClientName,
            quote.Title,
            quote.IssuedOn,
            quote.ValidUntil,
            quote.IsExpired,
            quote.Notes,
            quote.EventName,
            quote.Subtotal,
            quote.DiscountAmount,
            quote.Total,
            quote.Items,
            clock.UtcNow);

        return Result.Success(proposals.Write(proposal));
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>
    /// Numeração automática por ano: ORC-2026-0001. Em branco, o serviço escolhe;
    /// informado, respeita a escolha e só recusa repetido.
    /// </summary>
    private async Task<Result<string>> ResolveNumberAsync(Guid ownerId, string? requested, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var trimmed = requested.Trim();

            return await quotes.NumberExistsAsync(ownerId, trimmed, ct)
                ? Result.Failure<string>(Error.Conflict(
                    "quote.duplicate_number", $"Já existe um orçamento com o número {trimmed}."))
                : Result.Success(trimmed);
        }

        var year = Today.Year;
        var next = await quotes.CountInYearAsync(ownerId, year, ct) + 1;

        // Contar não basta se algo já foi excluído: tenta os seguintes até achar
        // um livre, em vez de estourar a unicidade no banco.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{NumberPrefix}-{year:D4}-{next + attempt:D4}");

            if (!await quotes.NumberExistsAsync(ownerId, candidate, ct))
                return Result.Success(candidate);
        }

        return Result.Failure<string>(Error.Conflict(
            "quote.number_unavailable",
            "Não foi possível gerar um número livre. Informe o número manualmente."));
    }

    private async Task<Result> EnsureEventExistsAsync(Guid? eventId, Guid ownerId, CancellationToken ct)
    {
        if (eventId is not { } id) return Result.Success();

        var @event = await events.GetByIdAsync(id, ownerId, ct);

        return @event is null
            ? Result.Failure(Error.NotFound("event.not_found", "Evento não encontrado."))
            : Result.Success();
    }

    private async Task<Result<Quote>> LoadAsync(Guid quoteId, CancellationToken ct)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<Quote>(NoSession);

        var quote = await quotes.GetByIdAsync(quoteId, ownerId, ct);

        return quote is null
            ? Result.Failure<Quote>(QuoteNotFound())
            : Result.Success(quote);
    }

    private async Task<Result<QuoteResponse>> MutateAsync(
        Guid quoteId,
        Action<Quote> mutate,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(quoteId, ct);
        if (loaded.IsFailure) return Result.Failure<QuoteResponse>(loaded.Error!);

        mutate(loaded.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(quoteId, loaded.Value.OwnerId, ct);
    }

    private async Task<Result<QuoteResponse>> ReadAsync(Guid quoteId, Guid ownerId, CancellationToken ct)
    {
        var response = await queries.GetAsync(quoteId, ownerId, Today, ct);

        return response is null
            ? Result.Failure<QuoteResponse>(QuoteNotFound())
            : Result.Success(response);
    }
}
