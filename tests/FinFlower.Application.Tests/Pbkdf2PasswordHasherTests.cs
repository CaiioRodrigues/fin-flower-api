using FinFlower.Infrastructure.Security;
using FluentAssertions;

namespace FinFlower.Application.Tests;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_never_contains_the_plain_password()
    {
        var hash = _hasher.Hash("Senha#Forte1");

        hash.Should().NotContain("Senha#Forte1");
        hash.Should().StartWith("v1.210000.", "o formato carrega a versão e o custo para permitir migração futura");
    }

    [Fact]
    public void Same_password_produces_different_hashes()
    {
        var first = _hasher.Hash("Senha#Forte1");
        var second = _hasher.Hash("Senha#Forte1");

        // Sal aleatório por senha: duas contas com a mesma senha não se denunciam,
        // e uma rainbow table não serve para nada.
        first.Should().NotBe(second);
        _hasher.Verify("Senha#Forte1", first).Should().BeTrue();
        _hasher.Verify("Senha#Forte1", second).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_the_wrong_password()
    {
        var hash = _hasher.Hash("Senha#Forte1");

        _hasher.Verify("Senha#Forte2", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-um-hash")]
    [InlineData("v1.210000.sal-invalido")]
    [InlineData("v2.210000.AAAA.BBBB")]
    [InlineData("v1.zero.AAAA.BBBB")]
    public void Verify_rejects_malformed_hashes_without_throwing(string hash)
    {
        _hasher.Verify("Senha#Forte1", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_a_tampered_hash()
    {
        var hash = _hasher.Hash("Senha#Forte1");
        var parts = hash.Split('.');
        var tampered = string.Join('.', parts[0], parts[1], parts[2], Convert.ToBase64String(new byte[64]));

        _hasher.Verify("Senha#Forte1", tampered).Should().BeFalse();
    }
}
