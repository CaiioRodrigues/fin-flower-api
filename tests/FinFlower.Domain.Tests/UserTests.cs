using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static User NewUser() => new("Caio", "Caio@Example.COM", "hash");

    [Fact]
    public void Email_is_normalized_to_lowercase()
    {
        NewUser().Email.Should().Be("caio@example.com");
    }

    [Fact]
    public void Account_locks_after_five_failed_attempts()
    {
        var user = NewUser();

        for (var i = 0; i < 4; i++)
        {
            user.RegisterFailedLogin(Now);
            user.IsLockedOut(Now).Should().BeFalse($"ainda faltam tentativas na {i + 1}ª falha");
        }

        user.RegisterFailedLogin(Now);

        user.IsLockedOut(Now).Should().BeTrue();
        user.LockoutEndsAt.Should().Be(Now.AddMinutes(15));
    }

    [Fact]
    public void Lockout_expires_after_the_window()
    {
        var user = NewUser();
        for (var i = 0; i < 5; i++) user.RegisterFailedLogin(Now);

        user.IsLockedOut(Now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void Successful_login_clears_the_failure_counter()
    {
        var user = NewUser();
        user.RegisterFailedLogin(Now);
        user.RegisterFailedLogin(Now);

        user.RegisterSuccessfulLogin();

        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEndsAt.Should().BeNull();
    }

    [Fact]
    public void Invalid_email_is_rejected()
    {
        var act = () => new User("Caio", "   ", "hash");

        act.Should().Throw<DomainException>().WithMessage("*e-mail*");
    }
}
