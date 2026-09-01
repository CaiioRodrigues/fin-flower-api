namespace FinFlower.Application.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
}

/// <summary>
/// Falha esperada de caso de uso. A API mapeia <see cref="Type"/> para o status HTTP,
/// então a camada de aplicação não precisa conhecer HTTP.
/// </summary>
public sealed record Error(ErrorType Type, string Code, string Message)
{
    public static Error Validation(string code, string message) => new(ErrorType.Validation, code, message);
    public static Error NotFound(string code, string message) => new(ErrorType.NotFound, code, message);
    public static Error Conflict(string code, string message) => new(ErrorType.Conflict, code, message);
    public static Error Unauthorized(string code, string message) => new(ErrorType.Unauthorized, code, message);
    public static Error Forbidden(string code, string message) => new(ErrorType.Forbidden, code, message);
}
