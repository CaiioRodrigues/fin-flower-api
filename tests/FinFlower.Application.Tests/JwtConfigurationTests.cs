using FinFlower.Infrastructure.Security;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// A chave do JWT é o único segredo que impede a aplicação de subir, então a
/// experiência de configurá-la faz parte do produto: se o caminho documentado
/// não funcionar, ninguém consegue rodar o projeto pela primeira vez.
/// </summary>
public class JwtConfigurationTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FinFlower.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    [Fact]
    public void The_api_project_declares_a_user_secrets_id()
    {
        var csproj = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "FinFlower.Api", "FinFlower.Api.csproj"));

        // Sem esta propriedade o cofre de segredos não existe: 'dotnet user-secrets'
        // recusa o comando e o .NET não carrega nada na subida. O Visual Studio a
        // acrescenta sozinho, mas só na máquina de quem clicou — e ela some no
        // clone seguinte, levando a chave junto. Foi assim que a API parou de subir.
        csproj.Should().Contain("<UserSecretsId>",
            "sem UserSecretsId versionado, a chave do JWT se perde a cada clone");
    }

    [Fact]
    public void The_signing_key_is_not_committed()
    {
        var settings = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "FinFlower.Api", "appsettings.json"));

        settings.Should().Contain("\"SigningKey\": \"\"", "segredo não se versiona");
    }

    [Fact]
    public void A_missing_key_says_how_to_set_it()
    {
        var result = new ValidateJwtOptions().Validate(null, Valid(key: ""));

        result.Failed.Should().BeTrue();

        // A mensagem antiga era "The SigningKey field is required": verdadeira e
        // inútil para quem está subindo o projeto pela primeira vez.
        var message = string.Join('\n', result.Failures!);
        message.Should().Contain("user-secrets");
        message.Should().Contain("Gerenciar Segredos do Usuário");
        message.Should().Contain("Jwt__SigningKey");
    }

    [Fact]
    public void A_short_key_explains_why_it_matters()
    {
        var result = new ValidateJwtOptions().Validate(null, Valid(key: "curta-demais"));

        result.Failed.Should().BeTrue();
        string.Join('\n', result.Failures!).Should().Contain("HMAC-SHA256");
    }

    [Fact]
    public void A_key_of_exactly_the_minimum_is_accepted()
    {
        var key = new string('k', JwtOptions.MinimumKeyLength);

        new ValidateJwtOptions().Validate(null, Valid(key: key))
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void An_absurd_token_lifetime_is_refused(int minutes)
    {
        new ValidateJwtOptions().Validate(null, Valid(minutes: minutes))
            .Failed.Should().BeTrue();
    }

    private static JwtOptions Valid(string? key = null, int minutes = 15) => new()
    {
        Issuer = "fin-flower-api",
        Audience = "fin-flower-web",
        SigningKey = key ?? new string('k', 48),
        AccessTokenMinutes = minutes,
        RefreshTokenDays = 7,
    };
}
