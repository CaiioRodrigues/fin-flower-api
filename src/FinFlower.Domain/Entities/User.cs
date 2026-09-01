using FinFlower.Domain.Common;

namespace FinFlower.Domain.Entities;

public sealed class User : AuditableEntity
{
    public const int MaxNameLength = 120;
    public const int MaxEmailLength = 256;
    private const int MaxFailedAttempts = 5;

    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User() { } // EF Core

    public User(string name, string email, string passwordHash)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, "nome", MaxNameLength);
        Email = NormalizeEmail(email);
        PasswordHash = Guard.AgainstNullOrWhiteSpace(passwordHash, "senha", 512);
        IsActive = true;
    }

    public string Name { get; private set; } = null!;

    /// <summary>Sempre em minúsculas: é a chave única de login.</summary>
    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } until && until > now;

    /// <summary>
    /// Conta a tentativa errada e bloqueia a conta ao atingir o limite.
    /// É o que torna um ataque de força bruta inviável mesmo com senha fraca.
    /// </summary>
    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            LockoutEndsAt = now.Add(LockoutDuration);
            FailedLoginAttempts = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
    }

    public void ChangePassword(string passwordHash) =>
        PasswordHash = Guard.AgainstNullOrWhiteSpace(passwordHash, "senha", 512);

    public void Deactivate() => IsActive = false;

    public static string NormalizeEmail(string email)
    {
        var trimmed = Guard.AgainstNullOrWhiteSpace(email, "e-mail", MaxEmailLength);
        return trimmed.ToLowerInvariant();
    }
}
