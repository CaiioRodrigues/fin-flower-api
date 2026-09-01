namespace FinFlower.Domain.Common;

/// <summary>
/// Guardas de invariante do domínio. Falha aqui é <see cref="DomainException"/>,
/// nunca uma exceção genérica de runtime.
/// </summary>
internal static class Guard
{
    public static string AgainstNullOrWhiteSpace(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"O campo '{field}' é obrigatório.");

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new DomainException($"O campo '{field}' deve ter no máximo {maxLength} caracteres.");

        return trimmed;
    }

    public static decimal AgainstNonPositiveMoney(decimal value, string field)
    {
        if (value <= 0)
            throw new DomainException($"O campo '{field}' deve ser maior que zero.");

        // Dinheiro tem duas casas: normaliza na entrada para o total nunca
        // divergir da soma das partes por arredondamento.
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
