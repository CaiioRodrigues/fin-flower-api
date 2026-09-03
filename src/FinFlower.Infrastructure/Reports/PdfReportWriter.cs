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
    private const float PageMargin = 28f;

    // Larguras fixas para o que tem tamanho previsível. Coluna proporcional
    // deixava "R$ 2.666,67" quebrar em duas linhas quando havia muito texto ao lado.
    private const float MoneyColumnWidth = 78f;
    private const float DateColumnWidth = 62f;
    private const float CountColumnWidth = 48f;

    /// <summary>Espaço mínimo reservado às colunas de texto, que dividem o resto.</summary>
    private const float MinimumTextWidth = 90f;

    /// <summary>
    /// Piso do encolhimento. Abaixo disto o número fica ilegível, e é melhor
    /// deixar a tabela transbordar de forma visível do que entregar um PDF que
    /// ninguém consegue ler.
    /// </summary>
    private const float MinimumScale = 0.62f;
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Accent = Color.FromHex("#12813C");
    private static readonly Color HeaderFill = Color.FromHex("#EEF4F0");
    private static readonly Color Muted = Color.FromHex("#6B7A72");

    public ReportFormat Format => ReportFormat.Pdf;

    public ReportFile Write(ReportDocument document)
    {
        var landscape = document.Tables.Any(t => t.Columns.Count >= LandscapeColumnThreshold);

        // A largura útil da página decide se as colunas fixas cabem. Sem isto,
        // um relatório com muitas colunas de dinheiro não ficava apertado: o
        // QuestPDF lançava, e o download virava um 500.
        var usableWidth = (landscape ? PageSizes.A4.Height : PageSizes.A4.Width) - (PageMargin * 2);
        var scale = document.Tables.Count == 0
            ? 1f
            : document.Tables.Min(table => FitScale(table, usableWidth));

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(PageMargin);
                page.DefaultTextStyle(style => style.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Element(header => Header(header, document));
                page.Content().PaddingVertical(10).Element(content => Content(content, document, scale));

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

    /// <summary>
    /// Quanto as colunas fixas precisam encolher para a tabela caber. 1 quando
    /// já cabe; nunca abaixo de <see cref="MinimumScale"/>.
    /// </summary>
    private static float FitScale(ReportTable table, float usableWidth)
    {
        var fixedWidth = table.Columns.Sum(column => FixedWidthOf(column.Type));
        var textColumns = table.Columns.Count(column => FixedWidthOf(column.Type) == 0);
        var available = usableWidth - (textColumns * MinimumTextWidth);

        if (fixedWidth <= available || fixedWidth <= 0) return 1f;

        return Math.Max(MinimumScale, available / fixedWidth);
    }

    private static float FixedWidthOf(ReportColumnType type) => type switch
    {
        ReportColumnType.Money => MoneyColumnWidth,
        ReportColumnType.Date => DateColumnWidth,
        ReportColumnType.Count => CountColumnWidth,
        _ => 0f,
    };

    private static void Content(IContainer container, ReportDocument document, float scale) =>
        container.Column(column =>
        {
            column.Spacing(16);

            if (document.Metrics.Count > 0)
                column.Item().Element(metrics => Metrics(metrics, document.Metrics));

            foreach (var table in document.Tables.Where(t => t.Rows.Count > 0))
                column.Item().Element(item => Table(item, table, scale));
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

    private static void Table(IContainer container, ReportTable table, float scale) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text(table.Title).FontSize(12).SemiBold();

            column.Item().Table(grid =>
            {
                grid.ColumnsDefinition(columns =>
                {
                    foreach (var definition in table.Columns)
                    {
                        var width = FixedWidthOf(definition.Type);

                        // Só o texto disputa o espaço que sobra.
                        if (width == 0) columns.RelativeColumn();
                        else columns.ConstantColumn(width * scale);
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
                            .Text(definition.Header).SemiBold().FontSize(8 * scale);
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

                        // Apertar a coluna sem apertar a fonte devolveria o
                        // defeito que as larguras fixas resolveram: número
                        // quebrado no meio.
                        cell.FontSize(9 * scale);

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
