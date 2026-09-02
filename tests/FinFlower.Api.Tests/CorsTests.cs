using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FinFlower.Api.Tests;

/// <summary>
/// Sobe a aplicação no ambiente Development, onde vale a regra permissiva de
/// origem local. O banco em memória não entende migrations, então o
/// MigrateOnStartup do appsettings.Development.json é desligado aqui.
/// </summary>
public sealed class DevelopmentApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:MigrateOnStartup"] = "false",
            }));
    }
}

public class CorsTests
{
    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }

    private static string? AllowedOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.FirstOrDefault()
            : null;

    public class InDevelopment : IClassFixture<DevelopmentApiFactory>
    {
        private readonly DevelopmentApiFactory _factory;

        public InDevelopment(DevelopmentApiFactory factory) => _factory = factory;

        [Theory]
        [InlineData("http://localhost:5173")]
        [InlineData("http://localhost:5174")] // O Vite pula de porta quando a 5173 está ocupada.
        [InlineData("http://127.0.0.1:3000")]
        public async Task Any_local_origin_is_allowed(string origin)
        {
            var response = await _factory.CreateClient().SendAsync(Preflight(origin));

            AllowedOrigin(response).Should().Be(origin);
        }

        [Fact]
        public async Task An_external_origin_is_still_refused()
        {
            var response = await _factory.CreateClient().SendAsync(Preflight("https://site-malicioso.com"));

            // Sem o cabeçalho, o navegador bloqueia a resposta.
            AllowedOrigin(response).Should().BeNull();
        }
    }

    public class OutsideDevelopment : IClassFixture<ApiFactory>
    {
        private readonly ApiFactory _factory;

        public OutsideDevelopment(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task Only_the_configured_origin_is_allowed()
        {
            var client = _factory.CreateClient();

            var configured = await client.SendAsync(Preflight("http://localhost:5173"));
            var other = await client.SendAsync(Preflight("http://localhost:5174"));
            var external = await client.SendAsync(Preflight("https://site-malicioso.com"));

            AllowedOrigin(configured).Should().Be("http://localhost:5173");
            AllowedOrigin(other).Should().BeNull("fora de desenvolvimento vale só a lista explícita");
            AllowedOrigin(external).Should().BeNull();
        }
    }
}
