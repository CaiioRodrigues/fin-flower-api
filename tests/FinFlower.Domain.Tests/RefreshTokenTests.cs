using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken() => new(Guid.CreateVersion7(), "hash", Now, Now.AddDays(7));

    [Fact]
    public void Token_is_active_until_it_expires()
    {
        var token = NewToken();

        token.IsActive(Now.AddDays(6)).Should().BeTrue();
        token.IsActive(Now.AddDays(8)).Should().BeFalse();
    }

    [Fact]
    public void Revoked_token_is_never_active()
    {
        var token = NewToken();

        token.Revoke(Now.AddHours(1));

        token.IsActive(Now.AddHours(2)).Should().BeFalse();
        token.RevokedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_timestamp()
    {
        var token = NewToken();
        token.Revoke(Now.AddHours(1));

        token.Revoke(Now.AddHours(5));

        token.RevokedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Rotation_records_the_replacement_token()
    {
        var token = NewToken();
        var replacementId = Guid.CreateVersion7();

        token.Revoke(Now, replacementId);

        token.ReplacedByTokenId.Should().Be(replacementId);
    }

    [Fact]
    public void Expiration_must_be_in_the_future()
    {
        var act = () => new RefreshToken(Guid.CreateVersion7(), "hash", Now, Now.AddDays(-1));

        act.Should().Throw<DomainException>().WithMessage("*futura*");
    }
}
