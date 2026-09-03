using FinFlower.Domain.Enums;

namespace FinFlower.Application.Quotes.Dtos;

public sealed record CreateQuoteRequest(
    string ClientName,
    string Title,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    string? Notes,
    Guid? EventId,
    // Deixe em branco para a numeração automática do ano corrente.
    string? Number = null);

public sealed record UpdateQuoteRequest(
    string ClientName,
    string Title,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    string? Notes,
    Guid? EventId);

public sealed record QuoteItemRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Unit);

public sealed record ApplyDiscountRequest(decimal Amount);

/// <summary>
/// Aprovar é o momento em que a proposta vira dinheiro a receber: aqui se
/// define em quantas vezes e como, porque é isso que gera as parcelas.
/// </summary>
public sealed record ApproveQuoteRequest(
    PaymentMethod PaymentMethod,
    int InstallmentCount,
    DateOnly FirstDueDate,
    DateOnly SignedOn,
    // Em branco, usa o cliente do orçamento.
    string? Counterparty = null);

public sealed record QuoteItemResponse(
    Guid Id,
    int Position,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Unit,
    decimal Total);

public sealed record QuoteSummaryResponse(
    Guid Id,
    string Number,
    string ClientName,
    string Title,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    QuoteStatus Status,
    bool IsExpired,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    int ItemCount,
    Guid? EventId,
    string? EventName,
    Guid? ContractId);

public sealed record QuoteResponse(
    Guid Id,
    string Number,
    string ClientName,
    string Title,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    QuoteStatus Status,
    bool IsExpired,
    bool IsEditable,
    string? Notes,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    Guid? EventId,
    string? EventName,
    Guid? ContractId,
    IReadOnlyList<QuoteItemResponse> Items);

public sealed record QuoteFilter(QuoteStatus? Status = null, Guid? EventId = null, string? Search = null);
