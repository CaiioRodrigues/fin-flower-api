using FinFlower.Domain.Common;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Refresh token persistido. Guarda apenas o <b>hash</b> do token: quem lê o banco
/// não consegue reconstruir o valor original e se passar pelo usuário.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { } // EF Core

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
            throw new DomainException("O refresh token precisa de um usuário.");

        if (expiresAt <= createdAt)
            throw new DomainException("A expiração do refresh token deve ser futura.");

        UserId = userId;
        TokenHash = Guard.AgainstNullOrWhiteSpace(tokenHash, "token", 128);
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Token que substituiu este na rotação. Permite auditar a cadeia de uso.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, Guid? replacedByTokenId = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        ReplacedByTokenId = replacedByTokenId;
    }
}
