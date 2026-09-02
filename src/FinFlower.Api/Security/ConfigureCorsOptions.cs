using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace FinFlower.Api.Security;

/// <summary>
/// Monta a política de CORS a partir de <see cref="CorsSettings"/> resolvido pelo
/// DI. Ler a configuração aqui, e não na construção do builder, garante que
/// qualquer fonte registrada depois (variável de ambiente, user-secrets,
/// configuração de teste) seja respeitada — mesmo motivo do JWT.
/// </summary>
public sealed class ConfigureCorsOptions(IOptions<CorsSettings> settings, IHostEnvironment environment)
    : IConfigureOptions<CorsOptions>
{
    public const string PolicyName = "frontend";

    public void Configure(CorsOptions options) =>
        options.AddPolicy(PolicyName, policy =>
        {
            policy
                .WithHeaders("Authorization", "Content-Type")
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE");

            if (environment.IsDevelopment())
            {
                // A porta do front varia em desenvolvimento: o Vite pula para a
                // seguinte quando a 5173 está ocupada, e o bloqueio de CORS chega
                // ao navegador como um "Failed to fetch" que não explica nada.
                policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback);
            }
            else
            {
                // Fora de desenvolvimento, só a lista explícita: AllowAnyOrigin
                // deixaria qualquer site chamar a API em nome do usuário.
                policy.WithOrigins(settings.Value.AllowedOrigins);
            }
        });
}
