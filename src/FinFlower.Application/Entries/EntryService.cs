using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Entries;

public interface IEntryService
{
    Task<Result<LedgerPageResponse>> ListAsync(EntryFilter filter, int page, int pageSize, CancellationToken ct = default);
    Task<Result<LedgerEntryResponse>> GetAsync(Guid entryId, CancellationToken ct = default);
    Task<Result<LedgerEntryResponse>> CreateAsync(CreateEntryRequest request, CancellationToken ct = default);
    Task<Result<LedgerEntryResponse>> UpdateAsync(Guid entryId, UpdateEntryRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid entryId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>> ListCategoriesAsync(CancellationToken ct = default);
}

/// <summary>
/// O livro-caixa: entrada e saída de dinheiro, com ou sem evento.
///
/// O evento deixou de ser dono do lançamento, mas continua mandando em si mesmo:
/// antes de mexer em algo ligado a um evento, este serviço carrega o evento e
/// pergunta a ele — <see cref="Event.EnsureAcceptsChanges"/> — se ainda aceita
/// alteração. A regra segue no domínio; mudou apenas quem faz a pergunta.
/// </summary>
public sealed class EntryService(
    IEntryRepository entries,
    IEntryQueries queries,
    IEventRepository events,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IEntryService
{
    public const int MaxPageSize = 200;
    public const int DefaultPageSize = 50;

    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    private static Error EntryNotFound() =>
        Error.NotFound("entry.not_found", "Lançamento não encontrado.");

    private static Error EventNotFound() =>
        Error.NotFound("event.not_found", "Evento não encontrado.");

    public async Task<Result<LedgerPageResponse>> ListAsync(
        EntryFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<LedgerPageResponse>(NoSession);

        // Normaliza aqui, e não no endpoint: um pageSize absurdo vindo da query
        // string não pode virar um SELECT sem limite.
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        return Result.Success(await queries.ListAsync(ownerId, filter, safePage, safeSize, ct));
    }

    public async Task<Result<LedgerEntryResponse>> GetAsync(Guid entryId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<LedgerEntryResponse>(NoSession);

        var entry = await queries.GetAsync(entryId, ownerId, ct);

        return entry is null
            ? Result.Failure<LedgerEntryResponse>(EntryNotFound())
            : Result.Success(entry);
    }

    public async Task<Result<LedgerEntryResponse>> CreateAsync(
        CreateEntryRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<LedgerEntryResponse>(NoSession);

        var check = await EnsureEventAcceptsAsync(request.EventId, ownerId, ct);
        if (check.IsFailure) return Result.Failure<LedgerEntryResponse>(check.Error!);

        var entry = new Entry(
            ownerId,
            request.Type,
            request.Description,
            request.Amount,
            request.Category,
            request.OccurredOn,
            request.EventId);

        entries.Add(entry);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(entry.Id, ownerId, ct);
    }

    public async Task<Result<LedgerEntryResponse>> UpdateAsync(
        Guid entryId,
        UpdateEntryRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<LedgerEntryResponse>(NoSession);

        var entry = await entries.GetByIdAsync(entryId, ownerId, ct);
        if (entry is null) return Result.Failure<LedgerEntryResponse>(EntryNotFound());

        // Os dois lados: não se tira um lançamento de um evento fechado, nem se
        // põe um lançamento dentro de um.
        var origin = await EnsureEventAcceptsAsync(entry.EventId, ownerId, ct);
        if (origin.IsFailure) return Result.Failure<LedgerEntryResponse>(origin.Error!);

        if (request.EventId != entry.EventId)
        {
            var target = await EnsureEventAcceptsAsync(request.EventId, ownerId, ct);
            if (target.IsFailure) return Result.Failure<LedgerEntryResponse>(target.Error!);
        }

        entry.Update(
            request.Type,
            request.Description,
            request.Amount,
            request.Category,
            request.OccurredOn,
            request.EventId);

        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(entryId, ownerId, ct);
    }

    public async Task<Result> DeleteAsync(Guid entryId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId) return Result.Failure(NoSession);

        var entry = await entries.GetByIdAsync(entryId, ownerId, ct);
        if (entry is null) return Result.Failure(EntryNotFound());

        // Recusa o que veio de contrato: quem manda é a parcela.
        entry.EnsureRemovable();

        var check = await EnsureEventAcceptsAsync(entry.EventId, ownerId, ct);
        if (check.IsFailure) return check;

        entry.MarkAsDeleted(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<string>>> ListCategoriesAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<IReadOnlyList<string>>(NoSession);

        return Result.Success(await queries.ListCategoriesAsync(ownerId, ct));
    }

    /// <summary>
    /// Carrega o evento — se houver — e deixa ele decidir. Evento inexistente ou
    /// de outro dono é 404; evento fechado vira <c>DomainException</c> e 400.
    /// </summary>
    private async Task<Result> EnsureEventAcceptsAsync(Guid? eventId, Guid ownerId, CancellationToken ct)
    {
        if (eventId is not { } id) return Result.Success();

        var @event = await events.GetByIdAsync(id, ownerId, ct);
        if (@event is null) return Result.Failure(EventNotFound());

        @event.EnsureAcceptsChanges();
        return Result.Success();
    }

    private async Task<Result<LedgerEntryResponse>> ReadAsync(Guid entryId, Guid ownerId, CancellationToken ct)
    {
        var response = await queries.GetAsync(entryId, ownerId, ct);

        return response is null
            ? Result.Failure<LedgerEntryResponse>(EntryNotFound())
            : Result.Success(response);
    }
}
