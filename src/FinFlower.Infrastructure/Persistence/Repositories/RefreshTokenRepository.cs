using FinFlower.Application.Abstractions;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);
}
