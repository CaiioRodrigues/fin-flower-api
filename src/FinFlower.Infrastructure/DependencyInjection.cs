using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Queries;
using FinFlower.Infrastructure.Persistence.Repositories;
using FinFlower.Infrastructure.Security;
using FinFlower.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlower.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "A connection string 'Default' não foi configurada.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                // Falha transitória de rede/banco não deve derrubar a requisição.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
            }));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IEventQueries, EventQueries>();

        // Valida as opções na subida do processo: chave JWT ausente ou curta
        // derruba a aplicação no start, não na primeira tentativa de login.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<ITokenProvider, JwtTokenProvider>();

        // ICurrentUser é registrado pela camada de API: ler a identidade da
        // requisição HTTP é responsabilidade dela, não da infraestrutura.

        return services;
    }
}
