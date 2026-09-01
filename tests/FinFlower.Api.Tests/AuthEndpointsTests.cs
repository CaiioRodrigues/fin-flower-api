using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinFlower.Application.Auth.Dtos;
using FluentAssertions;

namespace FinFlower.Api.Tests;

public class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly RegisterRequest Registration = new("Caio", "caio@example.com", "Senha#Forte1");

    private HttpClient NewClient() => factory.CreateClient();

    private static RegisterRequest UniqueRegistration() =>
        Registration with { Email = $"user-{Guid.CreateVersion7():N}@example.com" };

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, RegisterRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", request ?? UniqueRegistration());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await NewClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var response = await NewClient().GetAsync("/health");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("Content-Security-Policy").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        var response = await NewClient().GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_rejects_a_forged_token()
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmYWxzbyJ9.assinatura-invalida");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_then_call_me_with_the_issued_token()
    {
        var client = NewClient();
        var session = await RegisterAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");

        me!.Id.Should().Be(session.User.Id);
        me.Email.Should().Be(session.User.Email);
    }

    [Fact]
    public async Task Register_rejects_a_weak_password_with_field_level_errors()
    {
        var response = await NewClient()
            .PostAsJsonAsync("/api/auth/register", UniqueRegistration() with { Password = "fraca" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("Password").And.Contain("8 caracteres");
    }

    [Fact]
    public async Task Register_rejects_an_invalid_email()
    {
        var response = await NewClient()
            .PostAsJsonAsync("/api/auth/register", UniqueRegistration() with { Email = "nao-e-email" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_registration_returns_conflict()
    {
        var client = NewClient();
        var request = UniqueRegistration();
        await RegisterAsync(client, request);

        var duplicate = await client.PostAsJsonAsync("/api/auth/register", request);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_returns_a_usable_session()
    {
        var client = NewClient();
        var request = UniqueRegistration();
        await RegisterAsync(client, request);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(request.Email, request.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        session.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_are_indistinguishable()
    {
        var client = NewClient();
        var request = UniqueRegistration();
        await RegisterAsync(client, request);

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(request.Email, "Senha#Errada1"));

        var unknownEmail = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest($"ninguem-{Guid.CreateVersion7():N}@example.com", request.Password));

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Corpos idênticos (fora o traceId): nada na resposta revela se o e-mail
        // tem conta, o que impede enumerar usuários pelo endpoint de login.
        static async Task<string?> DetailOf(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

        (await DetailOf(wrongPassword)).Should().Be(await DetailOf(unknownEmail));
    }

    [Fact]
    public async Task Refresh_issues_a_new_pair_and_invalidates_the_old_one()
    {
        var client = NewClient();
        var session = await RegisterAsync(client);

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_invalidates_the_refresh_token()
    {
        var client = NewClient();
        var session = await RegisterAsync(client);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(session.RefreshToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
