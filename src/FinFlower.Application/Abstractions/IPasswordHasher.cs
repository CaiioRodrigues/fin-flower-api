namespace FinFlower.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Comparação em tempo constante: não vaza informação pelo tempo de resposta.</summary>
    bool Verify(string password, string hash);
}
