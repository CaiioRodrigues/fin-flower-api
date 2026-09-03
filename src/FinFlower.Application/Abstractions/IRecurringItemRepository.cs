using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public interface IRecurringItemRepository
{
    Task<RecurringItem?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecurringItem>> ListAsync(
        Guid ownerId,
        RecurringFilter filter,
        CancellationToken cancellationToken = default);

    void Add(RecurringItem item);
}
