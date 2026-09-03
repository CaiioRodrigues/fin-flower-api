using FinFlower.Application.Reports.Export;
using FinFlower.Infrastructure.Reports;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// O gerador de PDF usa largura fixa nas colunas de valor, porque coluna
/// proporcional quebrava "R$ 2.666,67" em duas linhas. O risco do outro lado é
/// a soma das larguras não caber na página — e isso derrubava a geração inteira
/// com 500, em vez de apertar a tabela.
/// </summary>
public class PdfReportWriterTests
{
    static PdfReportWriterTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static ReportDocument WithMoneyColumns(int count)
    {
        var columns = new List<ReportColumn> { new("Mês") };
        columns.AddRange(Enumerable.Range(1, count)
            .Select(i => new ReportColumn($"Valor {i}", ReportColumnType.Money)));
        columns.Add(new ReportColumn("Qtd.", ReportColumnType.Count));

        var cells = new List<object?> { "set/2026" };
        cells.AddRange(Enumerable.Range(1, count).Select(object? (i) => 123_456.78m + i));
        cells.Add(7);

        return new ReportDocument(
            "teste",
            "Relatório largo",
            null,
            DateTimeOffset.UtcNow,
            [],
            [new ReportTable("Tabela", columns, [new ReportRow(cells)])]);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(14)]
    public void A_wide_table_still_produces_a_pdf(int moneyColumns)
    {
        var file = new PdfReportWriter().Write(WithMoneyColumns(moneyColumns));

        file.ContentType.Should().Be("application/pdf");
        file.Content.Take(4).Should().Equal("%PDF"u8.ToArray());
        file.Content.Length.Should().BeGreaterThan(500);
    }

    [Fact]
    public void The_monthly_report_shape_fits_the_page()
    {
        // As dez colunas de dinheiro do caixa mês a mês somam mais que a
        // largura útil da A4 em paisagem: é o caso que quebrava.
        var file = new PdfReportWriter().Write(WithMoneyColumns(10));

        file.Content.Take(4).Should().Equal("%PDF"u8.ToArray());
    }
}
