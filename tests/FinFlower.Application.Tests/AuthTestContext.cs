using FinFlower.Application.Abstractions;
using FinFlower.Application.Auth;
using FinFlower.Application.Common;
using FinFlower.Infrastructure.Persistence;
using FinFlower.Infrastructure.Persistence.Repositories;
using FinFlower.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinFlower.Application.Tests;

/// <summary>Relógio controlável: expiração e bloqueio precisam ser testáveis sem esperar.</summary>
public sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }
    public bool IsAuthenticated => UserId is not null;
}

/// <summary>
/// Monta o <see cref="AuthService"/> com os componentes reais (repositórios, hasher
/// e provedor de token) sobre um banco em memória. O que é testado é o comportamento
/// de verdade, não um conjunto de dublês.
/// </summary>
public sealed class AuthTestContext : IDisposable
{
    public AuthTestContext(DateTimeOffset? now = null)
    {
        Clock = new FakeClock(now ?? new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"auth-tests-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Context = new AppDbContext(options, Clock);

        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "fin-flower-tests",
            Audience = "fin-flower-tests",
            SigningKey = "chave-de-teste-com-mais-de-32-caracteres-para-hmac",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
        });

        TokenProvider = new JwtTokenProvider(jwtOptions, Clock);
        CurrentUser = new FakeCurrentUser();

        Service = new AuthService(
            new UserRepository(Context),
            new RefreshTokenRepository(Context),
            new Pbkdf2PasswordHasher(),
            TokenProvider,
            Clock,
            CurrentUser,
            Context);
    }

    public FakeClock Clock { get; }
    public AppDbContext Context { get; }
    public ITokenProvider TokenProvider { get; }
    public FakeCurrentUser CurrentUser { get; }
    public IAuthService Service { get; }

    public void Dispose() => Context.Dispose();
}
