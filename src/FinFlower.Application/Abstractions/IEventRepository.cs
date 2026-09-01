using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

/// <summary>
/// Lado de escrita: devolve o agregado completo para que as regras do domínio
/// sejam aplicadas. Toda operação exige o <c>ownerId</c> — não existe forma de
/// carregar um evento sem dizer de quem ele é.
/// </summary>
public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);
    void Add(Event @event);
}
