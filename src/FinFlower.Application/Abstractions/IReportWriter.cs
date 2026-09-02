using FinFlower.Application.Reports.Export;

namespace FinFlower.Application.Abstractions;

/// <summary>Gera o arquivo de um <see cref="ReportDocument"/> num formato.</summary>
public interface IReportWriter
{
    ReportFormat Format { get; }

    ReportFile Write(ReportDocument document);
}
