using FinFlower.Application.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlower.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<DependencyInjectionMarker>(ServiceLifetime.Singleton);
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}

/// <summary>Âncora para varrer o assembly sem depender de nomes de tipo.</summary>
internal sealed class DependencyInjectionMarker;
