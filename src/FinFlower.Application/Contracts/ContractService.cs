using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;

namespace FinFlower.Application.Contracts;

public interface IContractService
{
    Task<Result<IReadOnlyList<ContractSummaryResponse>>> ListAsync(ContractFilter filter, CancellationToken ct = default);
    Task<Result<ContractResponse>> GetAsync(Guid contractId, CancellationToken ct = default);
    Task<Result<ContractResponse>> CreateAsync(CreateContractRequest request, CancellationToken ct = default);
    Task<Result<ContractResponse>> UpdateAsync(Guid contractId, UpdateContractRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid contractId, CancellationToken ct = default);

    Task<Result<ContractResponse>> SettleInstallmentAsync(Guid contractId, int number, SettleInstallmentRequest request, CancellationToken ct = default);
    Task<Result<ContractResponse>> UnsettleInstallmentAsync(Guid contractId, int number, CancellationToken ct = default);
    Task<Result<ContractResponse>> CancelInstallmentAsync(Guid contractId, int number, CancellationToken ct = default);
    Task<Result<ContractResponse>> RescheduleInstallmentAsync(Guid contractId, int number, RescheduleInstallmentRequest request, CancellationToken ct = default);
    Task<Result<ContractResponse>> ChangeInstallmentAmountAsync(Guid contractId, int number, ChangeInstallmentAmountRequest request, CancellationToken ct = default);

    Task<Result<AttachmentResponse>> AttachDocumentAsync(Guid contractId, string fileName, byte[] content, CancellationToken ct = default);
    Task<Result<AttachmentContent>> DownloadAttachmentAsync(Guid contractId, CancellationToken ct = default);
    Task<Result> RemoveAttachmentAsync(Guid contractId, CancellationToken ct = default);
}

public sealed record ContractFilter(
    Guid? EventId = null,
    ContractDirection? Direction = null,
    bool? OnlyOpen = null);

public sealed class ContractService(
    IContractRepository contracts,
    IContractQueries queries,
    IEventRepository events,
    IEntryRepository entries,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IContractService
{
    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    private static Error ContractNotFound() =>
        Error.NotFound("contract.not_found", "Contrato não encontrado.");

    public async Task<Result<IReadOnlyList<ContractSummaryResponse>>> ListAsync(
        ContractFilter filter,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<IReadOnlyList<ContractSummaryResponse>>(NoSession);

        return Result.Success(await queries.ListAsync(ownerId, filter, Today, ct));
    }

    public async Task<Result<ContractResponse>> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<ContractResponse>(NoSession);

        return await ReadAsync(contractId, ownerId, ct);
    }

    public async Task<Result<ContractResponse>> CreateAsync(
        CreateContractRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<ContractResponse>(NoSession);

        // O evento é opcional: existe contrato de aluguel e de fornecedor que
        // não pertence a trabalho nenhum. Quando há, ele precisa existir e ser
        // do mesmo dono — contrato em evento alheio é 404, como tudo mais.
        var linked = await EnsureEventAcceptsAsync(request.EventId, ownerId, ct);
        if (linked.IsFailure) return Result.Failure<ContractResponse>(linked.Error!);

        var contract = new Contract(
            ownerId,
            request.Direction,
            request.Counterparty,
            request.Description,
            request.TotalAmount,
            request.PaymentMethod,
            request.InstallmentCount,
            request.FirstDueDate,
            request.SignedOn,
            request.EventId);

        contracts.Add(contract);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(contract.Id, ownerId, ct);
    }

    public Task<Result<ContractResponse>> UpdateAsync(
        Guid contractId,
        UpdateContractRequest request,
        CancellationToken ct = default) =>
        MutateAsync(contractId, contract => contract.UpdateDetails(
            request.Direction,
            request.Counterparty,
            request.Description,
            request.PaymentMethod,
            request.SignedOn,
            request.EventId), ct);

    public async Task<Result> DeleteAsync(Guid contractId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(contractId, ct);
        if (loaded.IsFailure) return Result.Failure(loaded.Error!);

        var contract = loaded.Value;

        // Apagar um contrato com parcela liquidada deixaria lançamentos no caixa
        // sem origem. Estornar primeiro é uma decisão de quem opera, não nossa.
        if (contract.Installments.Any(i => i.Status == InstallmentStatus.Settled))
        {
            return Result.Failure(Error.Conflict(
                "contract.has_settled_installments",
                "Este contrato tem parcelas liquidadas. Estorne-as antes de excluí-lo."));
        }

        contract.MarkAsDeleted(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<ContractResponse>> SettleInstallmentAsync(
        Guid contractId,
        int number,
        SettleInstallmentRequest request,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(contractId, ct);
        if (loaded.IsFailure) return Result.Failure<ContractResponse>(loaded.Error!);

        var contract = loaded.Value;

        var linked = await EnsureEventAcceptsAsync(contract.EventId, contract.OwnerId, ct);
        if (linked.IsFailure) return Result.Failure<ContractResponse>(linked.Error!);

        // Em branco, valem o valor e a data da própria parcela: o caso comum é
        // pagar o combinado, e o formulário chega pré-preenchido com isso.
        var installment = contract.FindInstallment(number);
        var settledOn = request.SettledOn ?? installment.DueDate;
        var amount = request.Amount ?? installment.Amount;

        var entry = contract.SettleInstallment(
            number,
            settledOn,
            amount,
            request.Description,
            request.Category ?? "Contratos");

        entries.Add(entry);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(contractId, contract.OwnerId, ct);
    }

    public async Task<Result<ContractResponse>> UnsettleInstallmentAsync(
        Guid contractId,
        int number,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(contractId, ct);
        if (loaded.IsFailure) return Result.Failure<ContractResponse>(loaded.Error!);

        var contract = loaded.Value;

        var linked = await EnsureEventAcceptsAsync(contract.EventId, contract.OwnerId, ct);
        if (linked.IsFailure) return Result.Failure<ContractResponse>(linked.Error!);

        // A parcela devolve o lançamento que criou, e ele sai junto: estornar
        // desfaz previsto e realizado de uma vez.
        var entryId = contract.UnsettleInstallment(number);

        var entry = await entries.GetByIdAsync(entryId, contract.OwnerId, ct);
        entry?.MarkAsDeleted(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(contractId, contract.OwnerId, ct);
    }

    public Task<Result<ContractResponse>> CancelInstallmentAsync(
        Guid contractId,
        int number,
        CancellationToken ct = default) =>
        MutateAsync(contractId, contract => contract.CancelInstallment(number), ct);

    public Task<Result<ContractResponse>> RescheduleInstallmentAsync(
        Guid contractId,
        int number,
        RescheduleInstallmentRequest request,
        CancellationToken ct = default) =>
        MutateAsync(contractId, contract => contract.RescheduleInstallment(number, request.DueDate), ct);

    public Task<Result<ContractResponse>> ChangeInstallmentAmountAsync(
        Guid contractId,
        int number,
        ChangeInstallmentAmountRequest request,
        CancellationToken ct = default) =>
        MutateAsync(contractId, contract => contract.ChangeInstallmentAmount(number, request.Amount), ct);

    public async Task<Result<AttachmentResponse>> AttachDocumentAsync(
        Guid contractId,
        string fileName,
        byte[] content,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<AttachmentResponse>(NoSession);

        var contract = await contracts.GetWithAttachmentAsync(contractId, ownerId, ct);
        if (contract is null) return Result.Failure<AttachmentResponse>(ContractNotFound());

        var attachment = contract.AttachDocument(fileName, content, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(attachment));
    }

    public async Task<Result<AttachmentContent>> DownloadAttachmentAsync(
        Guid contractId,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<AttachmentContent>(NoSession);

        var contract = await contracts.GetWithAttachmentAsync(contractId, ownerId, ct);
        if (contract is null) return Result.Failure<AttachmentContent>(ContractNotFound());

        return contract.Attachment is not { } attachment
            ? Result.Failure<AttachmentContent>(
                Error.NotFound("contract.no_attachment", "Este contrato não tem documento anexado."))
            : Result.Success(new AttachmentContent(attachment.FileName, attachment.Content));
    }

    public async Task<Result> RemoveAttachmentAsync(Guid contractId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure(NoSession);

        var contract = await contracts.GetWithAttachmentAsync(contractId, ownerId, ct);
        if (contract is null) return Result.Failure(ContractNotFound());

        contract.RemoveAttachment();
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    /// <summary>
    /// Quando o contrato pertence a um evento, é o evento que diz se ainda
    /// aceita movimentação. Sem evento, não há o que perguntar.
    /// </summary>
    private async Task<Result> EnsureEventAcceptsAsync(Guid? eventId, Guid ownerId, CancellationToken ct)
    {
        if (eventId is not { } id) return Result.Success();

        var @event = await events.GetByIdAsync(id, ownerId, ct);
        if (@event is null)
            return Result.Failure(Error.NotFound("event.not_found", "Evento não encontrado."));

        @event.EnsureAcceptsChanges();
        return Result.Success();
    }

    private async Task<Result<Contract>> LoadAsync(Guid contractId, CancellationToken ct)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<Contract>(NoSession);

        var contract = await contracts.GetByIdAsync(contractId, ownerId, ct);

        return contract is null
            ? Result.Failure<Contract>(ContractNotFound())
            : Result.Success(contract);
    }

    private async Task<Result<ContractResponse>> MutateAsync(
        Guid contractId,
        Action<Contract> mutate,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(contractId, ct);
        if (loaded.IsFailure) return Result.Failure<ContractResponse>(loaded.Error!);

        mutate(loaded.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return await ReadAsync(contractId, loaded.Value.OwnerId, ct);
    }

    /// <summary>
    /// Devolve o contrato pelo lado de leitura. Montar a resposta a partir do
    /// agregado obrigaria a carregar o PDF só para informar o nome do arquivo.
    /// </summary>
    private async Task<Result<ContractResponse>> ReadAsync(Guid contractId, Guid ownerId, CancellationToken ct)
    {
        var response = await queries.GetAsync(contractId, ownerId, Today, ct);

        return response is null
            ? Result.Failure<ContractResponse>(ContractNotFound())
            : Result.Success(response);
    }

    private static AttachmentResponse ToResponse(ContractAttachment attachment) =>
        new(attachment.FileName, attachment.SizeInBytes, attachment.UploadedAt);
}
