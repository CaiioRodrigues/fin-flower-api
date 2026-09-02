using ClosedXML.Excel;
using FinFlower.Application.Abstractions;
using FinFlower.Application.Reports.Export;

namespace FinFlower.Infrastructure.Reports;

/// <summary>
/// Gera a planilha. Valores vão como número e data de verdade, com formato
/// aplicado por cima — texto formatado impediria somar, ordenar e usar tabela
/// dinâmica, que é o motivo de exportar para Excel.
/// </summary>
public sealed class ExcelReportWriter : IReportWriter
{
    private const string MoneyFormat = "R$ #,##0.00";
    private const string DateFormat = "dd/mm/yyyy";
    private const int MaxSheetNameLength = 31;

    public ReportFormat Format => ReportFormat.Xlsx;

    public ReportFile Write(ReportDocument document)
    {
        using var workbook = new XLWorkbook();

        WriteSummary(workbook, document);
        foreach (var table in document.Tables) WriteTable(workbook, table);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return new ReportFile(
            $"{document.FileNameStem}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            buffer.ToArray());
    }

    private static void WriteSummary(XLWorkbook workbook, ReportDocument document)
    {
        var sheet = workbook.Worksheets.Add("Resumo");

        sheet.Cell(1, 1).Value = document.Title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);

        sheet.Cell(2, 1).Value = document.Subtitle ?? string.Empty;
        sheet.Cell(3, 1).Value = $"Gerado em {document.GeneratedAt.LocalDateTime:dd/MM/yyyy HH:mm}";
        sheet.Cell(3, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        var row = 5;
        foreach (var metric in document.Metrics)
        {
            sheet.Cell(row, 1).Value = metric.Label;
            sheet.Cell(row, 1).Style.Font.SetBold();
            sheet.Cell(row, 2).Value = metric.Value;
            row++;
        }

        sheet.Columns(1, 2).AdjustToContents();
    }

    private static void WriteTable(XLWorkbook workbook, ReportTable table)
    {
        var sheet = workbook.Worksheets.Add(SheetName(workbook, table.Title));

        for (var column = 0; column < table.Columns.Count; column++)
        {
            var header = sheet.Cell(1, column + 1);
            header.Value = table.Columns[column].Header;
            header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EEF4F0"));
            header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        for (var line = 0; line < table.Rows.Count; line++)
        {
            var row = table.Rows[line];

            for (var column = 0; column < table.Columns.Count && column < row.Cells.Count; column++)
            {
                var cell = sheet.Cell(line + 2, column + 1);
                SetValue(cell, row.Cells[column], table.Columns[column].Type);
                if (row.Emphasized) cell.Style.Font.SetBold();
            }
        }

        if (table.Rows.Count > 0)
        {
            // Congela o cabeçalho e liga o filtro: é o que se espera ao abrir
            // uma planilha de relatório.
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1, 1, table.Rows.Count + 1, table.Columns.Count).SetAutoFilter();
        }

        sheet.Columns(1, table.Columns.Count).AdjustToContents();
    }

    private static void SetValue(IXLCell cell, object? value, ReportColumnType type)
    {
        switch (value)
        {
            case null:
                return;

            case decimal money:
                cell.Value = money;
                cell.Style.NumberFormat.Format = MoneyFormat;
                return;

            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                return;

            case int count:
                cell.Value = count;
                return;

            default:
                cell.Value = value.ToString();
                if (type == ReportColumnType.Text) cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                return;
        }
    }

    /// <summary>
    /// O Excel limita o nome da aba a 31 caracteres, proíbe alguns símbolos e não
    /// aceita nomes repetidos.
    /// </summary>
    private static string SheetName(XLWorkbook workbook, string title)
    {
        var cleaned = new string([.. title.Where(c => !"[]:*?/\\".Contains(c, StringComparison.Ordinal))]);
        var name = cleaned.Length > MaxSheetNameLength ? cleaned[..MaxSheetNameLength] : cleaned;

        if (!workbook.Worksheets.Contains(name)) return name;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{name[..Math.Min(name.Length, MaxSheetNameLength - 3)]} {suffix}";
            if (!workbook.Worksheets.Contains(candidate)) return candidate;
        }
    }
}
