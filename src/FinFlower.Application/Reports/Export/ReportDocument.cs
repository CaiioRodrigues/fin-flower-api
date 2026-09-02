namespace FinFlower.Application.Reports.Export;

public enum ReportFormat
{
    Xlsx = 1,
    Pdf = 2,
}

/// <summary>
/// Como a célula deve ser apresentada. No Excel vira formato de número — o valor
/// continua sendo número de verdade, então dá para somar e filtrar.
/// </summary>
public enum ReportColumnType
{
    Text = 0,
    Money = 1,
    Date = 2,
    Count = 3,
}

public sealed record ReportColumn(string Header, ReportColumnType Type = ReportColumnType.Text);

/// <summary>Uma linha. <c>Emphasized</c> marca totais e subtotais.</summary>
public sealed record ReportRow(IReadOnlyList<object?> Cells, bool Emphasized = false);

public sealed record ReportTable(string Title, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportRow> Rows);

/// <summary>Número de destaque no topo do relatório.</summary>
public sealed record ReportMetric(string Label, string Value);

/// <summary>
/// Relatório em formato neutro. Excel e PDF apenas renderizam isto, então um
/// relatório novo não encosta nos geradores.
/// </summary>
public sealed record ReportDocument(
    string FileNameStem,
    string Title,
    string? Subtitle,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<ReportTable> Tables);

public sealed record ReportFile(string FileName, string ContentType, byte[] Content);
