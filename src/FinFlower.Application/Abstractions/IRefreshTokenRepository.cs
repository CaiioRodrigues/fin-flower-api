using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    void Add(RefreshToken token);
}
