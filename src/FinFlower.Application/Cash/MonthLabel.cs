using FinFlower.Domain.ValueObjects;

namespace FinFlower.Application.Cash;

/// <summary>
/// Nome do mês em português. Uma tabela fixa em vez de <c>CultureInfo</c>: o
/// rótulo do relatório não pode depender da cultura instalada no servidor —
/// no contêiner ele viraria "Sep" sem ninguém perceber.
/// </summary>
public static class MonthLabel
{
    private static readonly string[] Short =
    [
        "jan", "fev", "mar", "abr", "mai", "jun",
        "jul", "ago", "set", "out", "nov", "dez",
    ];

    private static readonly string[] Long =
    [
        "janeiro", "fevereiro", "março", "abril", "maio", "junho",
        "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
    ];

    /// <summary>"set/2026".</summary>
    public static string For(YearMonth competence) =>
        $"{Short[competence.Month - 1]}/{competence.Year}";

    /// <summary>"setembro de 2026".</summary>
    public static string Full(YearMonth competence) =>
        $"{Long[competence.Month - 1]} de {competence.Year}";
}
