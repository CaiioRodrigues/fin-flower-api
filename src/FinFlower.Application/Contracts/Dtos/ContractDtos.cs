using FinFlower.Domain.Enums;

namespace FinFlower.Application.Contracts.Dtos;

public sealed record CreateContractRequest(
    ContractDirection Direction,
    string Counterparty,
    string? Description,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    int InstallmentCount,
    DateOnly FirstDueDate,
    DateOnly SignedOn,
    // Opcional: há contrato que não pertence a evento nenhum.
    Guid? EventId = null);

public sealed record UpdateContractRequest(
    ContractDirection Direction,
    string Counterparty,
    string? Description,
    PaymentMethod PaymentMethod,
    DateOnly SignedOn,
    Guid? EventId = null);

/// <summary>
/// Liquidação da parcela. Os campos opcionais existem porque o pagamento real
/// pode não ser o previsto — desconto, juros ou data diferente da combinada.
/// Em branco, valem o valor e a data da própria parcela.
/// </summary>
public sealed record SettleInstallmentRequest(
    DateOnly? SettledOn = null,
    decimal? Amount = null,
    string? Category = null,
    string? Description = null);

public sealed record RescheduleInstallmentRequest(DateOnly DueDate);

public sealed record ChangeInstallmentAmountRequest(decimal Amount);

public sealed record InstallmentResponse(
    int Number,
    decimal Amount,
    DateOnly DueDate,
    InstallmentStatus Status,
    bool IsOverdue,
    DateOnly? SettledOn,
    decimal? SettledAmount,
    Guid? EntryId);

public sealed record AttachmentResponse(string FileName, int SizeInBytes, DateTimeOffset UploadedAt);

public sealed record ContractResponse(
    Guid Id,
    Guid? EventId,
    string? EventName,
    Guid? QuoteId,
    string? QuoteNumber,
    ContractDirection Direction,
    string Counterparty,
    string? Description,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    DateOnly SignedOn,
    decimal SettledAmount,
    decimal OpenAmount,
    decimal OverdueAmount,
    bool IsFullySettled,
    AttachmentResponse? Attachment,
    IReadOnlyList<InstallmentResponse> Installments);

public sealed record ContractSummaryResponse(
    Guid Id,
    Guid? EventId,
    string? EventName,
    ContractDirection Direction,
    string Counterparty,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    decimal SettledAmount,
    decimal OpenAmount,
    decimal OverdueAmount,
    DateOnly? NextDueDate,
    int InstallmentCount,
    bool HasAttachment);

/// <summary>Arquivo pronto para download.</summary>
public sealed record AttachmentContent(string FileName, byte[] Content);
