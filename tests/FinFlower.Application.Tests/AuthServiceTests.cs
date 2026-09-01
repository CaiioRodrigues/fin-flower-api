using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Application.Tests;

public class AuthServiceTests
{
    private const string Email = "caio@example.com";
    private const string Password = "Senha#Forte1";

    private static RegisterRequest Registration(string email = Email, string password = Password) =>
        new("Caio", email, password);

    [Fact]
    public async Task Register_creates_the_user_and_returns_a_session()
    {
        using var ctx = new AuthTestContext();

        var result = await ctx.Service.RegisterAsync(Registration());

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Value.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Value.User.Email.Should().Be(Email);

        var stored = await ctx.Context.Users.SingleAsync();
        stored.PasswordHash.Should().NotContain(Password, "a senha nunca é persistida em texto claro");
    }

    [Fact]
    public async Task Register_normalizes_the_email_and_blocks_duplicates()
    {
        using var ctx = new AuthTestContext();
        await ctx.Service.RegisterAsync(Registration());

        var duplicate = await ctx.Service.RegisterAsync(Registration("CAIO@EXAMPLE.COM"));

        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error!.Type.Should().Be(ErrorType.Conflict);
        duplicate.Error.Code.Should().Be("auth.email_taken");
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        using var ctx = new AuthTestContext();
        await ctx.Service.RegisterAsync(Registration());

        var result = await ctx.Service.LoginAsync(new LoginRequest(Email, Password));

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessTokenExpiresAt.Should().Be(ctx.Clock.UtcNow.AddMinutes(15));
    }

    [Fact]
    public async Task Login_with_a_wrong_password_and_with_an_unknown_email_fail_identically()
    {
        using var ctx = new AuthTestContext();
        await ctx.Service.RegisterAsync(Registration());

        var wrongPassword = await ctx.Service.LoginAsync(new LoginRequest(Email, "Senha#Errada1"));
        var unknownEmail = await ctx.Service.LoginAsync(new LoginRequest("ninguem@example.com", Password));

        // Mensagens distintas permitiriam descobrir quais e-mails têm conta.
        wrongPassword.Error!.Should().BeEquivalentTo(unknownEmail.Error!);
        wrongPassword.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Account_is_locked_after_five_wrong_passwords()
    {
        using var ctx = new AuthTestContext();
        await ctx.Service.RegisterAsync(Registration());

        for (var i = 0; i < 5; i++)
            await ctx.Service.LoginAsync(new LoginRequest(Email, "Senha#Errada1"));

        // Mesmo com a senha correta, a conta está bloqueada.
        var blocked = await ctx.Service.LoginAsync(new LoginRequest(Email, Password));

        blocked.IsFailure.Should().BeTrue();
        blocked.Error!.Code.Should().Be("auth.locked_out");
        blocked.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Lockout_lifts_after_the_waiting_period()
    {
        using var ctx = new AuthTestContext();
        await ctx.Service.RegisterAsync(Registration());
        for (var i = 0; i < 5; i++)
            await ctx.Service.LoginAsync(new LoginRequest(Email, "Senha#Errada1"));

        ctx.Clock.Advance(TimeSpan.FromMinutes(16));
        var result = await ctx.Service.LoginAsync(new LoginRequest(Email, Password));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_revokes_the_previous_one()
    {
        using var ctx = new AuthTestContext();
        var session = (await ctx.Service.RegisterAsync(Registration())).Value;

        var refreshed = await ctx.Service.RefreshAsync(new RefreshRequest(session.RefreshToken));

        refreshed.IsSuccess.Should().BeTrue();
        refreshed.Value.RefreshToken.Should().NotBe(session.RefreshToken);

        var oldHash = ctx.TokenProvider.HashRefreshToken(session.RefreshToken);
        var old = await ctx.Context.RefreshTokens.SingleAsync(t => t.TokenHash == oldHash);
        old.RevokedAt.Should().NotBeNull();
        old.ReplacedByTokenId.Should().NotBeNull("a cadeia de rotação fica auditável");
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_chain()
    {
        using var ctx = new AuthTestContext();
        var session = (await ctx.Service.RegisterAsync(Registration())).Value;
        var rotated = (await ctx.Service.RefreshAsync(new RefreshRequest(session.RefreshToken))).Value;

        // O token antigo reaparecendo indica vazamento: tudo cai.
        var reuse = await ctx.Service.RefreshAsync(new RefreshRequest(session.RefreshToken));

        reuse.IsFailure.Should().BeTrue();
        reuse.Error!.Type.Should().Be(ErrorType.Unauthorized);

        var stillValid = await ctx.Service.RefreshAsync(new RefreshRequest(rotated.RefreshToken));
        stillValid.IsFailure.Should().BeTrue("o token legítimo também é invalidado ao detectar o reuso");

        var active = await ctx.Context.RefreshTokens.CountAsync(t => t.RevokedAt == null);
        active.Should().Be(0);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        using var ctx = new AuthTestContext();
        var session = (await ctx.Service.RegisterAsync(Registration())).Value;

        ctx.Clock.Advance(TimeSpan.FromDays(8));
        var result = await ctx.Service.RefreshAsync(new RefreshRequest(session.RefreshToken));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Unknown_refresh_token_is_rejected()
    {
        using var ctx = new AuthTestContext();
        await ctx.Service.RegisterAsync(Registration());

        var result = await ctx.Service.RefreshAsync(new RefreshRequest("token-inventado"));

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_never_leaks_whether_it_existed()
    {
        using var ctx = new AuthTestContext();
        var session = (await ctx.Service.RegisterAsync(Registration())).Value;

        var logout = await ctx.Service.LogoutAsync(new RefreshRequest(session.RefreshToken));
        var logoutUnknown = await ctx.Service.LogoutAsync(new RefreshRequest("token-inventado"));

        logout.IsSuccess.Should().BeTrue();
        logoutUnknown.IsSuccess.Should().BeTrue();

        var afterLogout = await ctx.Service.RefreshAsync(new RefreshRequest(session.RefreshToken));
        afterLogout.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Me_returns_the_user_identified_by_the_token()
    {
        using var ctx = new AuthTestContext();
        var session = (await ctx.Service.RegisterAsync(Registration())).Value;
        ctx.CurrentUser.UserId = session.User.Id;

        var result = await ctx.Service.GetCurrentUserAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be(Email);
    }

    [Fact]
    public async Task Me_fails_when_there_is_no_authenticated_user()
    {
        using var ctx = new AuthTestContext();

        var result = await ctx.Service.GetCurrentUserAsync();

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Unauthorized);
    }
}
