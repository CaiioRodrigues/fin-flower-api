using System.ComponentModel.DataAnnotations;

namespace FinFlower.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Chave de assinatura. Nunca versionada: vem de user-secrets em desenvolvimento
    /// e de variável de ambiente em produção. Mínimo de 32 bytes exigido pelo HMAC-SHA256.
    /// </summary>
    [Required, MinLength(32, ErrorMessage = "A chave JWT deve ter ao menos 32 caracteres.")]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 7;
}
