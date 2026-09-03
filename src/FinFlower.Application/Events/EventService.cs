using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Events;

public interface IEventService
{
    Task<Result<IReadOnlyList<EventSummaryResponse>>> ListAsync(EventFilter filter, CancellationToken ct = default);
    Task<Result<EventDetailsResponse>> GetAsync(Guid eventId, CancellationToken ct = default);
    Task<Result<EventDetailsResponse>> CreateAsync(CreateEventRequest request, CancellationToken ct = default);
    Task<Result<EventDetailsResponse>> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid eventId, CancellationToken ct = default);
    Task<Result<EventDetailsResponse>> CloseAsync(Guid eventId, CancellationToken ct = default);
    Task<Result<EventDetailsResponse>> ReopenAsync(Guid eventId, CancellationToken ct = default);
}

/// <summary>
/// Casos de uso de evento. O evento agrupa lançamentos para apurar resultado por
/// trabalho — os lançamentos em si vivem no livro-caixa, em <c>EntryService</c>.
///
/// Divisão de responsabilidade com o domínio: aqui ficam os desfechos esperados
/// da aplicação (não encontrado, sem sessão), devolvidos como <see cref="Result"/>.
/// Violação de invariante — mexer em evento fechado — é lançada pelo domínio como
/// <c>DomainException</c> e vira 400 no middleware.
/// </summary>
public sealed class EventService(
    IEventRepository events,
    IEventQueries queries,
    IEntryRepository entries,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IEventService
{
    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    /// <summary>
    /// Um evento de outro usuário responde 404, não 403: confirmar que o id
    /// existe já entregaria informação a quem está sondando.
    /// </summary>
    private static Error EventNotFound() =>
        Error.NotFound("event.not_found", "Evento não encontrado.");

    public async Task<Result<IReadOnlyList<EventSummaryResponse>>> ListAsync(
        EventFilter filter,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<IReadOnlyList<EventSummaryResponse>>(NoSession);

        return Result.Success(await queries.ListAsync(ownerId, filter, ct));
    }

    public async Task<Result<EventDetailsResponse>> GetAsync(Guid eventId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<EventDetailsResponse>(NoSession);

        return await ReadAsync(eventId, ownerId, ct);
    }

    public async Task<Result<EventDetailsResponse>> CreateAsync(
        CreateEventRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<EventDetailsResponse>(NoSession);

        var @event = new Event(ownerId, request.Name, request.Description, request.EventDate);
        events.Add(@event);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(@event.Id, ownerId, ct);
    }

    public Task<Result<EventDetailsResponse>> UpdateAsync(
        Guid eventId,
        UpdateEventRequest request,
        CancellationToken ct = default) =>
        MutateAsync(eventId, @event =>
            @event.UpdateDetails(request.Name, request.Description, request.EventDate), ct);

    public async Task<Result> DeleteAsync(Guid eventId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(eventId, ct);
        if (loaded.IsFailure) return Result.Failure(loaded.Error!);

        // O lançamento é do caixa, não do evento: apagar o evento não pode fazer
        // dinheiro sumir, nem deixar lançamento apontando para um evento que já
        // não existe. Quem opera decide o destino de cada um.
        var linked = await entries.ListByEventAsync(eventId, loaded.Value.OwnerId, ct);
        if (linked.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                "event.has_entries",
                $"Este evento tem {linked.Count} lançamento(s). Mova-os ou exclua-os antes."));
        }

        loaded.Value.MarkAsDeleted(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public Task<Result<EventDetailsResponse>> CloseAsync(Guid eventId, CancellationToken ct = default) =>
        MutateAsync(eventId, @event => @event.Close(), ct);

    public Task<Result<EventDetailsResponse>> ReopenAsync(Guid eventId, CancellationToken ct = default) =>
        MutateAsync(eventId, @event => @event.Reopen(), ct);

    /// <summary>Carrega o agregado já filtrado pelo dono da sessão.</summary>
    private async Task<Result<Event>> LoadAsync(Guid eventId, CancellationToken ct)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<Event>(NoSession);

        var @event = await events.GetByIdAsync(eventId, ownerId, ct);

        return @event is null
            ? Result.Failure<Event>(EventNotFound())
            : Result.Success(@event);
    }

    private async Task<Result<EventDetailsResponse>> MutateAsync(
        Guid eventId,
        Action<Event> mutate,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(eventId, ct);
        if (loaded.IsFailure) return Result.Failure<EventDetailsResponse>(loaded.Error!);

        mutate(loaded.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(eventId, loaded.Value.OwnerId, ct);
    }

    /// <summary>
    /// Devolve o evento pelo lado de leitura: os totais são somados no banco a
    /// partir dos lançamentos, que já não vivem dentro do agregado.
    /// </summary>
    private async Task<Result<EventDetailsResponse>> ReadAsync(Guid eventId, Guid ownerId, CancellationToken ct)
    {
        var details = await queries.GetDetailsAsync(eventId, ownerId, ct);

        return details is null
            ? Result.Failure<EventDetailsResponse>(EventNotFound())
            : Result.Success(details);
    }
}
