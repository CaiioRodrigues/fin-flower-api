using System.Globalization;
using FinFlower.Application.Abstractions;
using FinFlower.Application.Reports.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinFlower.Infrastructure.Reports;

/// <summary>
/// Gera o PDF para impressão e envio. Tabelas largas usam paisagem, e o
/// cabeçalho se repete em toda página.
/// </summary>
public sealed class PdfReportWriter : IReportWriter
{
    private const int LandscapeColumnThreshold = 6;

    // Larguras fixas para o que tem tamanho previsível. Coluna proporcional
    // deixava "R$ 2.666,67" quebrar em duas linhas quando havia muito texto ao lado.
    private const float MoneyColumnWidth = 78f;
    private const float DateColumnWidth = 62f;
    private const float CountColumnWidth = 48f;
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Accent = Color.FromHex("#12813C");
    private static readonly Color HeaderFill = Color.FromHex("#EEF4F0");
    private static readonly Color Muted = Color.FromHex("#6B7A72");

    public ReportFormat Format => ReportFormat.Pdf;

    public ReportFile Write(ReportDocument document)
    {
        var landscape = document.Tables.Any(t => t.Columns.Count >= LandscapeColumnThreshold);

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Element(header => Header(header, document));
                page.Content().PaddingVertical(10).Element(content => Content(content, document));

                page.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();

        return new ReportFile($"{document.FileNameStem}.pdf", "application/pdf", bytes);
    }

    private static void Header(IContainer container, ReportDocument document) =>
        container.Column(column =>
        {
            column.Item().Text("Fin Flower").FontSize(11).SemiBold().FontColor(Accent);
            column.Item().Text(document.Title).FontSize(17).Bold();

            if (!string.IsNullOrWhiteSpace(document.Subtitle))
                column.Item().Text(document.Subtitle).FontColor(Muted);

            column.Item().Text($"Gerado em {document.GeneratedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm", Ptbr)}")
                .FontSize(8).FontColor(Muted);

            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Accent);
        });

    private static void Content(IContainer container, ReportDocument document) =>
        container.Column(column =>
        {
            column.Spacing(16);

            if (document.Metrics.Count > 0)
                column.Item().Element(metrics => Metrics(metrics, document.Metrics));

            foreach (var table in document.Tables.Where(t => t.Rows.Count > 0))
                column.Item().Element(item => Table(item, table));
        });

    private static void Metrics(IContainer container, IReadOnlyList<ReportMetric> metrics) =>
        container.Row(row =>
        {
            row.Spacing(8);

            foreach (var metric in metrics)
            {
                row.RelativeItem().Background(HeaderFill).Padding(8).Column(cell =>
                {
                    cell.Item().Text(metric.Label).FontSize(8).FontColor(Muted);
                    cell.Item().Text(metric.Value).FontSize(11).SemiBold();
                });
            }
        });

    private static void Table(IContainer container, ReportTable table) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text(table.Title).FontSize(12).SemiBold();

            column.Item().Table(grid =>
            {
                grid.ColumnsDefinition(columns =>
                {
                    foreach (var definition in table.Columns)
                    {
                        switch (definition.Type)
                        {
                            case ReportColumnType.Money: columns.ConstantColumn(MoneyColumnWidth); break;
                            case ReportColumnType.Date: columns.ConstantColumn(DateColumnWidth); break;
                            case ReportColumnType.Count: columns.ConstantColumn(CountColumnWidth); break;
                            // Só o texto disputa o espaço que sobra.
                            default: columns.RelativeColumn(); break;
                        }
                    }
                });

                // Repetido em toda página: sem isso, a segunda página fica sem
                // saber o que cada coluna significa.
                grid.Header(header =>
                {
                    foreach (var definition in table.Columns)
                    {
                        header.Cell()
                            .Background(HeaderFill)
                            .Padding(4)
                            .AlignedFor(definition.Type)
                            .Text(definition.Header).SemiBold().FontSize(8);
                    }
                });

                foreach (var row in table.Rows)
                {
                    for (var index = 0; index < table.Columns.Count; index++)
                    {
                        var value = index < row.Cells.Count ? row.Cells[index] : null;

                        var cell = grid.Cell()
                            .BorderBottom(0.5f)
                            .BorderColor(Colors.Grey.Lighten2)
                            .Padding(4)
                            .AlignedFor(table.Columns[index].Type)
                            .Text(Cell(value));

                        if (row.Emphasized) cell.SemiBold();
                    }
                }
            });
        });

    private static string Cell(object? value) => value switch
    {
        null => string.Empty,
        decimal money => money.ToString("C2", Ptbr),
        DateOnly date => date.ToString("dd/MM/yyyy", Ptbr),
        int count => count.ToString(Ptbr),
        _ => value.ToString() ?? string.Empty,
    };
}

file static class PdfAlignment
{
    /// <summary>Número e data à direita; texto à esquerda.</summary>
    public static IContainer AlignedFor(this IContainer container, ReportColumnType type) =>
        type == ReportColumnType.Text ? container.AlignLeft() : container.AlignRight();
}
