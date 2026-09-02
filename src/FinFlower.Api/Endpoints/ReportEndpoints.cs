using FinFlower.Api.Extensions;
using FinFlower.Application.Reports;
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

        return app;
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
