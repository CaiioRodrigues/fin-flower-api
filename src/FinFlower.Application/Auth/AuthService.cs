using FinFlower.Application.Abstractions;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Common;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserResponse>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    ITokenProvider tokenProvider,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : IAuthService
{
    /// <summary>
    /// Resposta única para e-mail inexistente, senha errada e conta inativa.
    /// Diferenciar essas mensagens entrega ao atacante a lista de e-mails cadastrados.
    /// </summary>
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "E-mail ou senha inválidos.");

    private readonly Lazy<string> _dummyHash = new(() => passwordHasher.Hash("dummy-password-for-timing"));

    public async Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = User.NormalizeEmail(request.Email);

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            return Result.Failure<AuthResponse>(
                Error.Conflict("auth.email_taken", "Já existe uma conta com este e-mail."));
        }

        var user = new User(request.Name, email, passwordHasher.Hash(request.Password));
        users.Add(user);

        var response = IssueTokens(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            // Verifica contra um hash descartável para que o tempo de resposta de
            // "e-mail não existe" seja igual ao de "senha errada".
            passwordHasher.Verify(request.Password, _dummyHash.Value);
            return Result.Failure<AuthResponse>(InvalidCredentials);
        }

        var now = clock.UtcNow;

        if (user.IsLockedOut(now))
        {
            return Result.Failure<AuthResponse>(Error.Forbidden(
                "auth.locked_out",
                "Conta temporariamente bloqueada por excesso de tentativas. Tente novamente em alguns minutos."));
        }

        if (!user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.RegisterFailedLogin(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthResponse>(InvalidCredentials);
        }

        user.RegisterSuccessfulLogin();
        var response = IssueTokens(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }

    public async Task<Result<AuthResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var hash = tokenProvider.HashRefreshToken(request.RefreshToken);
        var stored = await refreshTokens.GetByHashAsync(hash, cancellationToken);

        if (stored is null)
            return Result.Failure<AuthResponse>(InvalidRefreshToken());

        if (!stored.IsActive(now))
        {
            // Um token já revogado sendo reapresentado indica que ele vazou. A cadeia
            // inteira do usuário cai, forçando um novo login.
            if (stored.RevokedAt is not null)
                await RevokeAllTokensAsync(stored.UserId, now, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthResponse>(InvalidRefreshToken());
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            stored.Revoke(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthResponse>(InvalidRefreshToken());
        }

        var response = IssueTokens(user, rotatedFrom: stored);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(response);
    }

    public async Task<Result> LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = tokenProvider.HashRefreshToken(request.RefreshToken);
        var stored = await refreshTokens.GetByHashAsync(hash, cancellationToken);

        // Sempre sucesso: o cliente não precisa saber se o token existia, e a
        // resposta não serve para sondar tokens válidos.
        if (stored is not null && stored.IsActive(clock.UtcNow))
        {
            stored.Revoke(clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<UserResponse>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return Result.Failure<UserResponse>(InvalidCredentials);

        var user = await users.GetByIdAsync(userId, cancellationToken);

        return user is null || !user.IsActive
            ? Result.Failure<UserResponse>(InvalidCredentials)
            : Result.Success(ToResponse(user));
    }

    private AuthResponse IssueTokens(User user, RefreshToken? rotatedFrom = null)
    {
        var now = clock.UtcNow;
        var access = tokenProvider.CreateAccessToken(user);
        var refresh = tokenProvider.CreateRefreshToken();

        var entity = new RefreshToken(user.Id, refresh.Hash, now, refresh.ExpiresAt);
        refreshTokens.Add(entity);
        rotatedFrom?.Revoke(now, entity.Id);

        return new AuthResponse(
            access.Value,
            access.ExpiresAt,
            refresh.Value,
            refresh.ExpiresAt,
            ToResponse(user));
    }

    private async Task RevokeAllTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var token in await refreshTokens.GetActiveByUserAsync(userId, cancellationToken))
            token.Revoke(now);
    }

    private static Error InvalidRefreshToken() =>
        Error.Unauthorized("auth.invalid_refresh_token", "Sessão expirada. Faça login novamente.");

    private static UserResponse ToResponse(User user) => new(user.Id, user.Name, user.Email);
}
