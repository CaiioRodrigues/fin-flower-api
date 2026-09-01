namespace FinFlower.Application.Common;

/// <summary>
/// Resultado explícito de um caso de uso. Erro de negócio é valor de retorno,
/// não exceção — o fluxo fica visível na assinatura do método.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Fail(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error? error) : base(isSuccess, error) => _value = value;

    /// <summary>Só pode ser lido em caso de sucesso; ler após falha é bug de chamador.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Não é possível ler o valor de um resultado com falha.");

    internal static Result<T> Ok(T value) => new(true, value, null);
    internal static Result<T> Fail(Error error) => new(false, default, error);
}
