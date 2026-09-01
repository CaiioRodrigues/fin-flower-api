namespace FinFlower.Application.Common;

/// <summary>
/// Identidade extraída do token da requisição. O id do usuário vem daqui e
/// nunca do corpo ou da URL — é o que impede acessar dado de outra pessoa
/// trocando um id na chamada.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}
