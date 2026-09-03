using FinFlower.Domain.Common;
using FinFlower.Domain.ValueObjects;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class YearMonthTests
{
    [Fact]
    public void Adding_months_rolls_the_year_over()
    {
        new YearMonth(2026, 11).AddMonths(3).Should().Be(new YearMonth(2027, 2));
        new YearMonth(2026, 1).AddMonths(-1).Should().Be(new YearMonth(2025, 12));
        new YearMonth(2026, 12).Next.Should().Be(new YearMonth(2027, 1));
    }

    [Fact]
    public void Adding_twelve_months_lands_on_the_same_month_a_year_later()
    {
        // A conta é feita em meses desde o ano zero: um erro de um a mais ou a
        // menos aqui deslocaria a série inteira do caixa.
        foreach (var month in Enumerable.Range(1, 12))
            new YearMonth(2026, month).AddMonths(12).Should().Be(new YearMonth(2027, month));
    }

    [Fact]
    public void Day_of_month_is_capped_at_the_end_of_short_months()
    {
        // Um gasto fixo que vence todo dia 31 não pode estourar em fevereiro.
        new YearMonth(2026, 2).DayOrLast(31).Should().Be(new DateOnly(2026, 2, 28));
        new YearMonth(2028, 2).DayOrLast(31).Should().Be(new DateOnly(2028, 2, 29), "2028 é bissexto");
        new YearMonth(2026, 4).DayOrLast(31).Should().Be(new DateOnly(2026, 4, 30));
        new YearMonth(2026, 3).DayOrLast(10).Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void Range_is_inclusive_on_both_ends()
    {
        var months = YearMonth.Range(new YearMonth(2026, 11), new YearMonth(2027, 2)).ToList();

        months.Select(m => m.ToString())
            .Should().Equal("2026-11", "2026-12", "2027-01", "2027-02");
    }

    [Fact]
    public void Range_of_a_single_month_yields_that_month()
    {
        YearMonth.Range(new YearMonth(2026, 9), new YearMonth(2026, 9))
            .Should().ContainSingle().Which.Should().Be(new YearMonth(2026, 9));
    }

    [Fact]
    public void Months_until_counts_both_directions()
    {
        new YearMonth(2026, 1).MonthsUntil(new YearMonth(2026, 12)).Should().Be(11);
        new YearMonth(2026, 12).MonthsUntil(new YearMonth(2026, 1)).Should().Be(-11);
    }

    [Fact]
    public void Comparison_orders_by_year_then_month()
    {
        (new YearMonth(2026, 1) < new YearMonth(2026, 2)).Should().BeTrue();
        (new YearMonth(2027, 1) > new YearMonth(2026, 12)).Should().BeTrue();
        (new YearMonth(2026, 5) <= new YearMonth(2026, 5)).Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-09", 2026, 9)]
    [InlineData("2026-1", 2026, 1)]
    [InlineData(" 2026-12 ", 2026, 12)]
    public void Parsing_accepts_the_competence_format(string text, int year, int month)
    {
        YearMonth.Parse(text).Should().Be(new YearMonth(year, month));
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("setembro")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1999-01")]
    public void Parsing_rejects_anything_else(string? text)
    {
        YearMonth.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_throws_a_domain_exception_with_the_expected_format()
    {
        var act = () => YearMonth.Parse("09/2026");

        act.Should().Throw<DomainException>().WithMessage("*aaaa-mm*");
    }

    [Fact]
    public void To_string_always_pads_the_month()
    {
        new YearMonth(2026, 9).ToString().Should().Be("2026-09");
        new YearMonth(2026, 10).ToString().Should().Be("2026-10");
    }
}
