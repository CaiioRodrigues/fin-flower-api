using FinFlower.Api.Extensions;
using FinFlower.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/reports")
            .WithTags("Relatórios")
            .RequireAuthorization()
            .MapGet("/cash", GetCashReport)
            .WithSummary("Caixa consolidado: entradas, saídas, saldo e quantos eventos deram lucro.");

        return app;
    }

    private static async Task<IResult> GetCashReport(
        ICashReportService service,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null) =>
        (await service.GetAsync(from, to, cancellationToken)).ToHttpResult();
}
