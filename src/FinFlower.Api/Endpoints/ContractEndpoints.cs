using FinFlower.Api.Extensions;
using FinFlower.Application.Contracts;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FinFlower.Api.Endpoints;

public static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var contracts = app.MapGroup("/api/contracts")
            .WithTags("Contratos")
            .RequireAuthorization();

        contracts.MapGet("/", List).WithSummary("Lista os contratos, com o quanto já foi liquidado.");
        contracts.MapPost("/", Create).WithSummary("Cria um contrato, com ou sem evento, e gera as parcelas.");
        contracts.MapGet("/{contractId:guid}", Get).WithSummary("Abre um contrato com suas parcelas.");
        contracts.MapPut("/{contractId:guid}", Update).WithSummary("Altera os dados do contrato.");
        contracts.MapDelete("/{contractId:guid}", Delete).WithSummary("Exclui o contrato.");

        var installments = contracts.MapGroup("/{contractId:guid}/installments/{number:int}")
            .WithTags("Parcelas");

        installments.MapPost("/settle", Settle)
            .WithSummary("Liquida a parcela e gera o lançamento correspondente no caixa.");
        installments.MapPost("/unsettle", Unsettle)
            .WithSummary("Estorna a parcela e remove o lançamento gerado.");
        installments.MapPost("/cancel", Cancel).WithSummary("Cancela a parcela.");
        installments.MapPut("/due-date", Reschedule).WithSummary("Altera o vencimento.");
        installments.MapPut("/amount", ChangeAmount)
            .WithSummary("Altera o valor, redistribuindo a diferença entre as parcelas em aberto.");

        var document = contracts.MapGroup("/{contractId:guid}/document").WithTags("Documento");

        document.MapPost("/", Upload)
            .WithSummary("Anexa o PDF do contrato.")
            .DisableAntiforgery();
        document.MapGet("/", Download).WithSummary("Baixa o PDF anexado.");
        document.MapDelete("/", RemoveDocument).WithSummary("Remove o PDF anexado.");

        return app;
    }

    private static async Task<IResult> List(
        IContractService service,
        CancellationToken cancellationToken,
        [FromQuery] Guid? eventId = null,
        [FromQuery] ContractDirection? direction = null,
        [FromQuery] bool? onlyOpen = null) =>
        (await service.ListAsync(new ContractFilter(eventId, direction, onlyOpen), cancellationToken))
            .ToHttpResult();

    private static async Task<IResult> Get(
        Guid contractId,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.GetAsync(contractId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Create(
        [FromBody] CreateContractRequest request,
        IValidator<CreateContractRequest> validator,
        IContractService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        var result = await service.CreateAsync(request, cancellationToken);
        return result.ToHttpResult(response => Results.Created($"/api/contracts/{response.Id}", response));
    }

    private static async Task<IResult> Update(
        Guid contractId,
        [FromBody] UpdateContractRequest request,
        IValidator<UpdateContractRequest> validator,
        IContractService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.UpdateAsync(contractId, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Delete(
        Guid contractId,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.DeleteAsync(contractId, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Settle(
        Guid contractId,
        int number,
        [FromBody] SettleInstallmentRequest request,
        IValidator<SettleInstallmentRequest> validator,
        IContractService service,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateRequestAsync(request, cancellationToken) is { } invalid) return invalid;

        return (await service.SettleInstallmentAsync(contractId, number, request, cancellationToken)).ToHttpResult();
    }

    private static async Task<IResult> Unsettle(
        Guid contractId,
        int number,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.UnsettleInstallmentAsync(contractId, number, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Cancel(
        Guid contractId,
        int number,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.CancelInstallmentAsync(contractId, number, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Reschedule(
        Guid contractId,
        int number,
        [FromBody] RescheduleInstallmentRequest request,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.RescheduleInstallmentAsync(contractId, number, request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> ChangeAmount(
        Guid contractId,
        int number,
        [FromBody] ChangeInstallmentAmountRequest request,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.ChangeInstallmentAmountAsync(contractId, number, request, cancellationToken)).ToHttpResult();

    private static async Task<IResult> Upload(
        Guid contractId,
        IFormFile file,
        IContractService service,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return Results.BadRequest(new { title = "Requisição inválida", detail = "O arquivo está vazio." });

        // O limite é conferido antes de ler: sem isso, um arquivo enorme já teria
        // sido carregado inteiro na memória quando o domínio fosse recusá-lo.
        if (file.Length > ContractAttachment.MaxSizeInBytes)
        {
            return Results.Json(
                new
                {
                    title = "Arquivo grande demais",
                    detail = $"O arquivo deve ter no máximo {ContractAttachment.MaxSizeInBytes / (1024 * 1024)} MB.",
                },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var result = await service.AttachDocumentAsync(
            contractId,
            file.FileName,
            buffer.ToArray(),
            cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> Download(
        Guid contractId,
        IContractService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DownloadAttachmentAsync(contractId, cancellationToken);

        return result.ToHttpResult(file => Results.File(
            file.Content,
            // Tipo fixo, nunca o declarado por quem enviou: deixar o navegador
            // interpretar um arquivo do usuário como HTML seria um XSS.
            ContractAttachment.PdfContentType,
            file.FileName));
    }

    private static async Task<IResult> RemoveDocument(
        Guid contractId,
        IContractService service,
        CancellationToken cancellationToken) =>
        (await service.RemoveAttachmentAsync(contractId, cancellationToken)).ToHttpResult();
}
