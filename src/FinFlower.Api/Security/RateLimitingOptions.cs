using System.ComponentModel.DataAnnotations;

namespace FinFlower.Api.Security;

/// <summary>
/// Limites de requisição por IP. Configuráveis porque o valor certo depende do
/// ambiente — atrás de um proxy corporativo, muitos usuários compartilham o mesmo IP.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requisições por janela nas rotas de credencial (login, registro, refresh).</summary>
    [Range(1, 10_000)]
    public int AuthPermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int AuthWindowSeconds { get; init; } = 60;

    [Range(1, 100_000)]
    public int GlobalPermitLimit { get; init; } = 120;

    [Range(1, 3600)]
    public int GlobalWindowSeconds { get; init; } = 60;
}
