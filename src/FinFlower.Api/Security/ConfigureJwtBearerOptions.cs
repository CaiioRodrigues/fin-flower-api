using System.Text;
using FinFlower.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinFlower.Api.Security;

/// <summary>
/// Configura a validação do JWT a partir de <see cref="JwtOptions"/> resolvido pelo DI.
/// Ler a configuração aqui, e não na construção do builder, garante que qualquer
/// fonte registrada depois (variável de ambiente, user-secrets, configuração de teste)
/// seja respeitada.
/// </summary>
public sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> options)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly JwtOptions _jwt = options.Value;

    public void Configure(JwtBearerOptions bearer)
    {
        // Sem o mapeamento legado, a claim 'sub' chega com o próprio nome.
        bearer.MapInboundClaims = false;
        bearer.SaveToken = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _jwt.Issuer,
            ValidAudience = _jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
            // Sem tolerância de relógio: token expirado é token expirado.
            ClockSkew = TimeSpan.Zero,
        };
    }

    public void Configure(string? name, JwtBearerOptions bearer) => Configure(bearer);
}
