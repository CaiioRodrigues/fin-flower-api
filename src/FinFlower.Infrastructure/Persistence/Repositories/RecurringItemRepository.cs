using FinFlower.Application.Abstractions;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class RecurringItemRepository(AppDbContext context) : IRecurringItemRepository
{
    public Task<RecurringItem?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        context.RecurringItems.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, cancellationToken);

    public async Task<IReadOnlyList<RecurringItem>> ListAsync(
        Guid ownerId,
        RecurringFilter filter,
        CancellationToken cancellationToken = default)
    {
        // Rastreado de propósito: a geração do mês chama GenerateEntry sobre
        // estas mesmas instâncias, e o caso de uso é curto.
        var query = context.RecurringItems.Where(r => r.OwnerId == ownerId);

        if (filter.Kind is { } kind) query = query.Where(r => r.Kind == kind);
        if (filter.OnlyActive == true) query = query.Where(r => r.IsActive);

        return await query
            // Ativos primeiro, depois por dia de vencimento: é a ordem em que
            // quem paga as contas do mês percorre a lista.
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.DayOfMonth)
            .ThenBy(r => r.Description)
            .ToListAsync(cancellationToken);
    }

    public void Add(RecurringItem item) => context.RecurringItems.Add(item);
}
