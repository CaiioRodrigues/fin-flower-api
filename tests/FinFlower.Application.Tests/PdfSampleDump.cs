using FinFlower.Application.Reports.Export;
using FinFlower.Infrastructure.Reports;

namespace FinFlower.Application.Tests;

/// <summary>Ferramenta de inspeção: gera um PDF com a forma do caixa mês a mês.</summary>
public class PdfSampleDump
{
    [Fact(Skip = "Inspeção manual. Rode com PDF_SAMPLE_DIR=/caminho e sem o Skip.")]
    public void Dump()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var directory = Environment.GetEnvironmentVariable("PDF_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        var columns = new List<ReportColumn>
        {
            new("Mês"),
            new("Saldo inicial", ReportColumnType.Money),
            new("Entradas", ReportColumnType.Money),
            new("Saídas", ReportColumnType.Money),
            new("Resultado", ReportColumnType.Money),
            new("Saldo final", ReportColumnType.Money),
            new("A receber", ReportColumnType.Money),
            new("A pagar", ReportColumnType.Money),
            new("Saldo projetado", ReportColumnType.Money),
            new("Custos fixos", ReportColumnType.Money),
            new("Pró-labore", ReportColumnType.Money),
            new("Lanç.", ReportColumnType.Count),
        };

        var rows = new List<ReportRow>();
        var balance = 0m;

        foreach (var index in Enumerable.Range(0, 12))
        {
            var income = 28_000m + (index * 1_940m);
            var expense = 24_830m + (index * 1_120m);
            var opening = balance;
            balance += income - expense;

            rows.Add(new ReportRow([
                $"{new[] { "out", "nov", "dez", "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set" }[index]}/2026",
                opening, income, expense, income - expense, balance,
                index > 8 ? 41_250.55m : 0m, index > 8 ? 8_500m : 0m, balance + 32_750.55m,
                3_710m, 6_000m, 7,
            ]));
        }

        rows.Add(new ReportRow(["Total do período", 0m, 403_100m, 332_662m, 70_438m, 70_438m,
            123_751.65m, 25_500m, 168_689.65m, 40_810m, 66_000m, 84], Emphasized: true));

        var file = new PdfReportWriter().Write(new ReportDocument(
            "caixa-mensal",
            "Caixa mês a mês",
            "De 2025-10 a 2026-09",
            DateTimeOffset.UtcNow,
            [
                new ReportMetric("Saldo inicial", "R$ 0,00"),
                new ReportMetric("Entradas", "R$ 403.100,00"),
                new ReportMetric("Saídas", "R$ 332.662,00"),
                new ReportMetric("Saldo final", "R$ 70.438,00"),
                new ReportMetric("Saldo projetado", "R$ 168.689,65"),
            ],
            [new ReportTable("Mês a mês", columns, rows)]));

        File.WriteAllBytes(Path.Combine(directory, file.FileName), file.Content);
    }
}
