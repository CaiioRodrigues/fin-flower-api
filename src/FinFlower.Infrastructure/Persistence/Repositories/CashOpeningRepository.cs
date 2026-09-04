using FinFlower.Application.Abstractions;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class CashOpeningRepository(AppDbContext context) : ICashOpeningRepository
{
    public Task<CashOpening?> GetAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
        context.CashOpenings.FirstOrDefaultAsync(o => o.OwnerId == ownerId, cancellationToken);

    public void Add(CashOpening opening) => context.CashOpenings.Add(opening);

    /// <summary>
    /// Apagar de verdade, não marcar como excluído: o índice único do dono é
    /// filtrado por IsDeleted, e um registro morto ali bloquearia o próximo.
    /// </summary>
    public void Remove(CashOpening opening) => context.CashOpenings.Remove(opening);
}
