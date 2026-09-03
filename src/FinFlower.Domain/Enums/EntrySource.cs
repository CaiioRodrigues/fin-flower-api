namespace FinFlower.Domain.Enums;

/// <summary>
/// De onde o lançamento veio. Define o que pode ser alterado nele: o que nasce
/// de uma parcela pertence ao contrato, o que nasce de um item fixo pode ser
/// ajustado (a conta de luz veio diferente do previsto) sem perder o vínculo.
/// </summary>
public enum EntrySource
{
    /// <summary>Digitado à mão no livro-caixa.</summary>
    Manual = 1,

    /// <summary>Gerado pela liquidação de uma parcela de contrato.</summary>
    Contract = 2,

    /// <summary>Gerado pela competência de um item fixo (gasto fixo ou pró-labore).</summary>
    Recurring = 3,
}
