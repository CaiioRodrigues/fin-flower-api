using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public interface ICashOpeningRepository
{
    Task<CashOpening?> GetAsync(Guid ownerId, CancellationToken cancellationToken = default);

    void Add(CashOpening opening);

    void Remove(CashOpening opening);
}
