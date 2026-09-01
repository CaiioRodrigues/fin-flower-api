namespace FinFlower.Domain.Common;

/// <summary>
/// Violação de uma regra de negócio. A API traduz para HTTP 400/409 —
/// o domínio não conhece HTTP.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
