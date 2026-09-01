using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    void Add(User user);
}
