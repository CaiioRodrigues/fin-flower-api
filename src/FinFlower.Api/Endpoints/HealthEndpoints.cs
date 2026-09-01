namespace FinFlower.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Infraestrutura")
            .WithSummary("Verificação de disponibilidade.")
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }
}
