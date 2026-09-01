using FinFlower.Api.Extensions;
using FinFlower.Application.Auth;
using FinFlower.Application.Auth.Dtos;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>Limite dedicado às rotas de credencial — ver <c>Program.cs</c>.</summary>
    public const string AuthRateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Autenticação")
            .RequireRateLimiting(AuthRateLimitPolicy);

        group.MapPost("/register", Register)
            .WithSummary("Cria uma conta e já devolve os tokens da sessão.")
            .AllowAnonymous();

        group.MapPost("/login", Login)
            .WithSummary("Autentica por e-mail e senha.")
            .AllowAnonymous();

        group.MapPost("/refresh", Refresh)
            .WithSummary("Troca um refresh token válido por um novo par de tokens.")
            .AllowAnonymous();

        group.MapPost("/logout", Logout)
            .WithSummary("Revoga o refresh token informado.")
            .AllowAnonymous();

        group.MapGet("/me", Me)
            .WithSummary("Dados do usuário autenticado.")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest request,
        IValidator<RegisterRequest> validator,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await auth.RegisterAsync(request, cancellationToken);
        return result.ToHttpResult(response => Results.Created("/api/auth/me", response));
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        IValidator<LoginRequest> validator,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await auth.LoginAsync(request, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Refresh(
        [FromBody] RefreshRequest request,
        IValidator<RefreshRequest> validator,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await auth.RefreshAsync(request, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Logout(
        [FromBody] RefreshRequest request,
        IValidator<RefreshRequest> validator,
        IAuthService auth,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await auth.LogoutAsync(request, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Me(IAuthService auth, CancellationToken cancellationToken) =>
        (await auth.GetCurrentUserAsync(cancellationToken)).ToHttpResult();
}
