using System.Globalization;
using FinFlower.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinFlower.Api.Tests;

/// <summary>
/// Sobe a aplicação real — pipeline, autenticação e endpoints — trocando apenas
/// o SQL Server por um banco em memória. O que roda aqui é o mesmo
/// <c>Program.cs</c> de produção.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"api-tests-{Guid.CreateVersion7()}";

    /// <summary>
    /// Alto por padrão para não travar os testes funcionais, que compartilham o
    /// mesmo IP. <see cref="RateLimitingTests"/> baixa o valor de propósito.
    /// </summary>
    protected virtual int AuthPermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=nao-usado;Database=FinFlower;Trusted_Connection=True",
                ["Jwt:Issuer"] = "fin-flower-tests",
                ["Jwt:Audience"] = "fin-flower-tests",
                ["Jwt:SigningKey"] = "chave-de-teste-com-mais-de-32-caracteres-para-hmac",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "7",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["RateLimiting:AuthPermitLimit"] = AuthPermitLimit.ToString(CultureInfo.InvariantCulture),
                ["RateLimiting:GlobalPermitLimit"] = "10000",
            }));

        builder.ConfigureServices(services =>
        {
            // A partir do EF Core 9 o provider entra por IDbContextOptionsConfiguration.
            // Remover só DbContextOptions deixaria SQL Server e InMemory registrados juntos.
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
