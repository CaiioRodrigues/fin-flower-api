using FinFlower.Api.Extensions;
using FinFlower.Application.Common;
using FinFlower.Application.Reports;
using FinFlower.Application.Reports.Export;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/reports")
            .WithTags("Relatórios")
            .RequireAuthorization();

        reports.MapGet("/cash", GetCashReport)
            .WithSummary("Caixa consolidado: entradas, saídas, saldo e quantos eventos deram lucro.");

        reports.MapGet("/cash-flow", GetCashFlow)
            .WithSummary("Fluxo de caixa: vencidos, o mês corrente e a previsão dos próximos.");

        reports.MapGet("/monthly/export", ExportMonthlyCash)
            .WithSummary("Baixa o caixa mês a mês, com saldo acumulado, em xlsx ou pdf.");

        reports.MapGet("/cash/export", ExportCash)
            .WithSummary("Baixa o caixa por evento em xlsx ou pdf.");

        reports.MapGet("/cash-flow/export", ExportCashFlow)
            .WithSummary("Baixa o fluxo de caixa em xlsx ou pdf.");

        reports.MapGet("/installments/export", ExportInstallments)
            .WithSummary("Baixa as parcelas em aberto, a receber e a pagar, em xlsx ou pdf.");

        app.MapGet("/api/events/{eventId:guid}/statement/export", ExportStatement)
            .WithTags("Relatórios")
            .WithSummary("Baixa o extrato do evento, com lançamentos, contratos e parcelas.")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ExportMonthlyCash(
        IReportExportService service,
        CancellationToken cancellationToken,
        [FromQuery] string format = "xlsx",
        [FromQuery] string? from = null,
        [FromQuery] string? to = null) =>
        await Export(format, chosen => service.ExportMonthlyCashAsync(chosen, from, to, cancellationToken));

    private static async Task<IResult> ExportCash(
        IReportExportService service,
        CancellationToken cancellationToken,
        [FromQuery] string format = "xlsx",
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null) =>
        await Export(format, chosen => service.ExportCashAsync(chosen, from, to, cancellationToken));

    private static async Task<IResult> ExportCashFlow(
        IReportExportService service,
        CancellationToken cancellationToken,
        [FromQuery] string format = "xlsx",
        [FromQuery] int monthsAhead = 6) =>
        await Export(format, chosen => service.ExportCashFlowAsync(chosen, monthsAhead, cancellationToken));

    private static async Task<IResult> ExportInstallments(
        IReportExportService service,
        CancellationToken cancellationToken,
        [FromQuery] string format = "xlsx") =>
        await Export(format, chosen => service.ExportInstallmentsAsync(chosen, cancellationToken));

    private static async Task<IResult> ExportStatement(
        Guid eventId,
        IReportExportService service,
        CancellationToken cancellationToken,
        [FromQuery] string format = "pdf") =>
        await Export(format, chosen => service.ExportEventStatementAsync(eventId, chosen, cancellationToken));

    /// <summary>Traduz o formato pedido e devolve o arquivo como download.</summary>
    private static async Task<IResult> Export(
        string format,
        Func<ReportFormat, Task<Result<ReportFile>>> export)
    {
        if (!Enum.TryParse<ReportFormat>(format, ignoreCase: true, out var chosen))
        {
            return Results.Problem(
                title: "Requisição inválida",
                detail: "Formato inválido. Use 'xlsx' ou 'pdf'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await export(chosen);

        return result.ToHttpResult(file => Results.File(file.Content, file.ContentType, file.FileName));
    }

    private static async Task<IResult> GetCashFlow(
        ICashFlowReportService service,
        CancellationToken cancellationToken,
        [FromQuery] int monthsAhead = 6) =>
        (await service.GetAsync(monthsAhead, cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetCashReport(
        ICashReportService service,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null) =>
        (await service.GetAsync(from, to, cancellationToken)).ToHttpResult();
}
