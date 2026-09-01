using System.Net;
using System.Net.Http.Json;
using FinFlower.Application.Auth.Dtos;
using FluentAssertions;

namespace FinFlower.Api.Tests;

/// <summary>Fábrica com o limite propositalmente baixo, para provar o bloqueio.</summary>
public sealed class ThrottledApiFactory : ApiFactory
{
    protected override int AuthPermitLimit => 3;
}

public class RateLimitingTests(ThrottledApiFactory factory) : IClassFixture<ThrottledApiFactory>
{
    [Fact]
    public async Task Credential_endpoints_start_returning_429_after_the_limit()
    {
        var client = factory.CreateClient();
        var attempt = new LoginRequest("ninguem@example.com", "Senha#Errada1");

        // Dentro do limite: falha de credencial, não bloqueio.
        for (var i = 0; i < 3; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/auth/login", attempt);
            allowed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login", attempt);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
