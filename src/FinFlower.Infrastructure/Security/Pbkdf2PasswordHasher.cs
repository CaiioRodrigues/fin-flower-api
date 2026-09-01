using System.Security.Cryptography;
using FinFlower.Application.Abstractions;

namespace FinFlower.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 com os parâmetros recomendados pela OWASP.
/// O hash carrega algoritmo, iterações e sal, então dá para aumentar o custo
/// no futuro sem invalidar as senhas já cadastradas.
/// Formato: <c>v1.{iterações}.{sal}.{hash}</c>
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 64;
    private const string Prefix = "v1";
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return string.Join('.', Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;

        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != Prefix) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        // Comparação em tempo fixo: um `==` vazaria o tamanho do prefixo correto
        // pelo tempo de resposta.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
