using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinFlower.Application.Common;

namespace FinFlower.Api.Security;

/// <summary>
/// Lê a identidade do token já validado pelo middleware de autenticação.
/// Nada aqui vem do corpo da requisição — é essa separação que impede um cliente
/// de se declarar outro usuário.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
