using System.Globalization;

namespace FinFlower.Domain.ValueObjects;

/// <summary>
/// Uma competência: ano e mês, sem dia. É o eixo do sistema — todo número do
/// caixa é apurado por mês, e usar <see cref="DateOnly"/> para isso convidaria
/// a comparações erradas ("setembro" virando "01/09 às 00:00" e deixando o
/// resto do mês de fora).
/// </summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    public const int MinYear = 2000;
    public const int MaxYear = 2200;

    public YearMonth(int year, int month)
    {
        if (year is < MinYear or > MaxYear)
            throw new Common.DomainException($"O ano deve estar entre {MinYear} e {MaxYear}.");

        if (month is < 1 or > 12)
            throw new Common.DomainException("O mês deve estar entre 1 e 12.");

        Year = year;
        Month = month;
    }

    public int Year { get; }
    public int Month { get; }

    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);

    public static YearMonth Current(DateOnly today) => From(today);

    /// <summary>Primeiro dia da competência — a forma com que ela é gravada.</summary>
    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    /// <summary>
    /// O dia pedido dentro desta competência, limitado ao fim do mês: um item
    /// que vence todo dia 31 cai em 28/02 sem estourar.
    /// </summary>
    public DateOnly DayOrLast(int day) => new(Year, Month, Math.Min(day, DaysInMonth));

    public YearMonth AddMonths(int months)
    {
        var zeroBased = ((Year * 12) + Month - 1) + months;
        return new YearMonth(zeroBased / 12, (zeroBased % 12) + 1);
    }

    public YearMonth Next => AddMonths(1);
    public YearMonth Previous => AddMonths(-1);

    /// <summary>Quantos meses separam as duas competências (negativo se <paramref name="other"/> é posterior).</summary>
    public int MonthsUntil(YearMonth other) =>
        ((other.Year - Year) * 12) + (other.Month - Month);

    /// <summary>A sequência fechada de competências entre as duas, inclusive.</summary>
    public static IEnumerable<YearMonth> Range(YearMonth from, YearMonth to)
    {
        for (var current = from; current <= to; current = current.Next)
            yield return current;
    }

    public static YearMonth Parse(string value) =>
        TryParse(value, out var result)
            ? result
            : throw new Common.DomainException(
                $"'{value}' não é uma competência válida. Use o formato aaaa-mm, como 2026-09.");

    public static bool TryParse(string? value, out YearMonth result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('-');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)) return false;
        if (year is < MinYear or > MaxYear || month is < 1 or > 12) return false;

        result = new YearMonth(year, month);
        return true;
    }

    public int CompareTo(YearMonth other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;
    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;
    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}
