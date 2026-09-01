using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinFlower.Infrastructure.Security;

public sealed class JwtTokenProvider(IOptions<JwtOptions> options, IDateTimeProvider clock) : ITokenProvider
{
    private const int RefreshTokenBytes = 32;

    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    public AccessToken CreateAccessToken(User user)
    {
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.Name),
            // jti único permite rastrear e, se preciso, revogar um access token específico.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshTokenPair CreateRefreshToken()
    {
        // 256 bits de um gerador criptográfico: não é adivinhável nem previsível.
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

        return new RefreshTokenPair(
            value,
            HashRefreshToken(value),
            clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    /// <summary>
    /// SHA-256 direto (sem sal) porque o token já é aleatório de 256 bits — não há
    /// dicionário a proteger, e o hash determinístico permite a busca por índice.
    /// </summary>
    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
