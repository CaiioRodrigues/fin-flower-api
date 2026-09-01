namespace FinFlower.Api.Middleware;

/// <summary>
/// Cabeçalhos de defesa em profundidade. A API responde JSON, então a CSP é
/// restritiva ao máximo — nada deve ser carregado a partir das respostas dela.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        // O Swagger é uma página real e precisa carregar os próprios assets;
        // a CSP travada vale para as respostas da API, que são só JSON.
        if (!context.Request.Path.StartsWithSegments("/swagger"))
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

        // Resposta autenticada não pode ficar em cache compartilhado.
        headers["Cache-Control"] = "no-store";

        return next(context);
    }
}
