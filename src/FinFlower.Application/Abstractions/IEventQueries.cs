using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Reports.Dtos;

namespace FinFlower.Application.Abstractions;

/// <summary>
/// Lado de leitura. Projeta direto para DTO no banco em vez de carregar o
/// agregado inteiro — a listagem e o caixa somam em SQL, não em memória.
/// Como toda assinatura exige <c>ownerId</c>, é impossível montar uma consulta
/// que enxergue dado de outro usuário.
/// </summary>
public interface IEventQueries
{
    Task<IReadOnlyList<EventSummaryResponse>> ListAsync(
        Guid ownerId,
        EventFilter filter,
        CancellationToken cancellationToken = default);

    Task<EventDetailsResponse?> GetDetailsAsync(
        Guid eventId,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<CashReportResponse> GetCashReportAsync(
        Guid ownerId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);
}
