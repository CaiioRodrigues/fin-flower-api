namespace FinFlower.Application.Quotes.Dtos;

/// <summary>
/// Tudo que a proposta impressa precisa, num objeto só. O gerador do PDF não
/// consulta nada: recebe isto pronto e desenha.
/// </summary>
public sealed record QuoteProposal(
    string Number,
    string IssuerName,
    string IssuerEmail,
    string ClientName,
    string Title,
    DateOnly IssuedOn,
    DateOnly ValidUntil,
    bool IsExpired,
    string? Notes,
    string? EventName,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    IReadOnlyList<QuoteItemResponse> Items,
    DateTimeOffset GeneratedAt);
