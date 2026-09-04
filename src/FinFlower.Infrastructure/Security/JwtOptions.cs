namespace FinFlower.Infrastructure.Security;

/// <summary>
/// As regras de validação estão em <see cref="ValidateJwtOptions"/>, e não em
/// anotações: a mensagem que o desenvolvedor lê quando a aplicação não sobe
/// precisa dizer o que fazer, e "The SigningKey field is required" não diz.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Tamanho da chave do HMAC-SHA256: 256 bits.</summary>
    public const int MinimumKeyLength = 32;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Chave de assinatura. Nunca versionada: vem de user-secrets em desenvolvimento
    /// e de variável de ambiente em produção.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 7;
}
