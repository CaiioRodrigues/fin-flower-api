using Microsoft.Extensions.Options;

namespace FinFlower.Infrastructure.Security;

/// <summary>
/// Valida as opções do JWT com mensagens que dizem o que fazer.
///
/// A validação por anotação dava "The SigningKey field is required", o que é
/// verdade e não ajuda ninguém: quem está subindo o projeto pela primeira vez
/// não tem como adivinhar que a chave mora em user-secrets. A mensagem aqui
/// carrega os comandos.
/// </summary>
public sealed class ValidateJwtOptions : IValidateOptions<JwtOptions>
{
    private const string HowToSetTheKey = """
        A chave de assinatura do JWT não está configurada, e sem ela a aplicação não sobe.
        Ela fica fora do repositório de propósito — segredo não se versiona.

        No Visual Studio:
          botão direito no projeto FinFlower.Api -> Gerenciar Segredos do Usuário, e cole:
          { "Jwt": { "SigningKey": "<pelo menos 32 caracteres>" } }

        Na linha de comando, da raiz do repositório:
          dotnet user-secrets set "Jwt:SigningKey" "<pelo menos 32 caracteres>" --project src/FinFlower.Api

        Em produção, use a variável de ambiente Jwt__SigningKey.
        """;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(HowToSetTheKey);
        }
        else if (options.SigningKey.Length < JwtOptions.MinimumKeyLength)
        {
            // O HMAC-SHA256 usa uma chave de 256 bits. Uma chave mais curta é
            // preenchida com zeros, e o token passa a valer menos do que aparenta.
            failures.Add(
                $"A chave do JWT tem {options.SigningKey.Length} caracteres e precisa de ao menos "
                + $"{JwtOptions.MinimumKeyLength}. Abaixo disso o HMAC-SHA256 completa o resto com "
                + "zeros, e a assinatura fica mais fraca do que parece.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add("Jwt:Issuer é obrigatório — está em appsettings.json.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add("Jwt:Audience é obrigatório — está em appsettings.json.");

        if (options.AccessTokenMinutes is < 1 or > 60)
            failures.Add("Jwt:AccessTokenMinutes deve estar entre 1 e 60.");

        if (options.RefreshTokenDays is < 1 or > 90)
            failures.Add("Jwt:RefreshTokenDays deve estar entre 1 e 90.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
