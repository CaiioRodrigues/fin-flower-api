namespace FinFlower.Domain.Common;

public static class MoneySplit
{
    /// <summary>
    /// Divide um valor em parcelas de centavos inteiros cuja soma é exatamente o
    /// total. Dividir e arredondar cada parcela perderia ou criaria centavos:
    /// 1000 em 3 daria 333,33 três vezes, e o contrato fecharia em 999,99.
    /// A sobra vai para as últimas parcelas — 333,33 / 333,33 / 333,34.
    /// </summary>
    public static IReadOnlyList<decimal> Into(decimal total, int parts)
    {
        if (parts < 1)
            throw new DomainException("O contrato precisa de ao menos uma parcela.");

        var cents = (long)decimal.Round(total * 100, 0, MidpointRounding.AwayFromZero);
        if (cents < parts)
            throw new DomainException("O valor é baixo demais para ser dividido nessa quantidade de parcelas.");

        var baseCents = cents / parts;
        var remainder = (int)(cents % parts);

        return [.. Enumerable.Range(0, parts).Select(index =>
            (baseCents + (index >= parts - remainder ? 1 : 0)) / 100m)];
    }
}
