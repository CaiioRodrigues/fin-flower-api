using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Par de refresh token: o valor vai para o cliente, o hash para o banco.</summary>
public sealed record RefreshTokenPair(string Value, string Hash, DateTimeOffset ExpiresAt);

public interface ITokenProvider
{
    AccessToken CreateAccessToken(User user);
    RefreshTokenPair CreateRefreshToken();
    string HashRefreshToken(string token);
}
