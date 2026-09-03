using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Um orçamento: a proposta montada linha a linha, antes de existir contrato.
/// Aprovado, vira um <see cref="Contract"/> com o mesmo valor — é a ponte entre
/// o que foi vendido e o que vai entrar no caixa.
/// </summary>
public sealed class Quote : AuditableEntity
{
    public const int MaxNumberLength = 30;
    public const int MaxClientLength = 160;
    public const int MaxTitleLength = 160;
    public const int MaxNotesLength = 2000;
    public const int MaxItems = 200;

    private readonly List<QuoteItem> _items = [];

    private Quote() { } // EF Core

    public Quote(
        Guid ownerId,
        string number,
        string clientName,
        string title,
        DateOnly issuedOn,
        DateOnly validUntil,
        string? notes,
        Guid? eventId)
    {
        if (ownerId == Guid.Empty) throw new DomainException("O orçamento precisa de um dono.");

        OwnerId = ownerId;
        Number = Guard.AgainstNullOrWhiteSpace(number, "número", MaxNumberLength);
        Status = QuoteStatus.Draft;
        EventId = eventId;

        Apply(clientName, title, issuedOn, validUntil, notes);
    }

    public Guid OwnerId { get; private set; }

    /// <summary>Número visível ao cliente, único por dono.</summary>
    public string Number { get; private set; } = null!;

    public string ClientName { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public DateOnly IssuedOn { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public QuoteStatus Status { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Evento a que a proposta se refere, quando já existe.</summary>
    public Guid? EventId { get; private set; }

    /// <summary>Contrato gerado na aprovação. É o que impede aprovar duas vezes.</summary>
    public Guid? ContractId { get; private set; }

    /// <summary>Desconto sobre o subtotal, em dinheiro.</summary>
    public decimal DiscountAmount { get; private set; }

    public IReadOnlyCollection<QuoteItem> Items => _items.AsReadOnly();

    /// <summary>Os itens na ordem em que o cliente lê a proposta.</summary>
    public IReadOnlyList<QuoteItem> OrderedItems => [.. _items.OrderBy(i => i.Position)];

    public decimal Subtotal => _items.Sum(i => i.Total);

    /// <summary>O que o cliente paga: subtotal menos desconto, nunca negativo.</summary>
    public decimal Total => Math.Max(0m, Subtotal - DiscountAmount);

    /// <summary>Vencido é leitura da data, não estado guardado — nenhuma rotina
    /// precisa varrer o banco quando o dia vira.</summary>
    public bool IsExpired(DateOnly today) =>
        Status is QuoteStatus.Draft or QuoteStatus.Sent && ValidUntil < today;

    /// <summary>Enquanto não foi decidido, o orçamento ainda pode ser mexido.</summary>
    public bool IsEditable => Status is QuoteStatus.Draft or QuoteStatus.Sent;

    public void UpdateDetails(
        string clientName,
        string title,
        DateOnly issuedOn,
        DateOnly validUntil,
        string? notes)
    {
        EnsureEditable();
        Apply(clientName, title, issuedOn, validUntil, notes);
    }

    public QuoteItem AddItem(string description, decimal quantity, decimal unitPrice, string? unit)
    {
        EnsureEditable();

        if (_items.Count >= MaxItems)
            throw new DomainException($"O orçamento aceita no máximo {MaxItems} itens.");

        var item = new QuoteItem(Id, NextPosition(), description, quantity, unitPrice, unit);
        _items.Add(item);
        return item;
    }

    public void UpdateItem(Guid itemId, string description, decimal quantity, decimal unitPrice, string? unit)
    {
        EnsureEditable();
        FindItem(itemId).Update(description, quantity, unitPrice, unit);
    }

    public void RemoveItem(Guid itemId)
    {
        EnsureEditable();
        _items.Remove(FindItem(itemId));
        Renumber();
    }

    public void ApplyDiscount(decimal amount)
    {
        EnsureEditable();

        if (amount < 0) throw new DomainException("O desconto não pode ser negativo.");

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded > Subtotal)
            throw new DomainException("O desconto não pode ser maior que o subtotal.");

        DiscountAmount = rounded;
    }

    public void MarkAsSent()
    {
        if (Status != QuoteStatus.Draft)
            throw new DomainException("Só um orçamento em rascunho pode ser enviado.");

        if (_items.Count == 0)
            throw new DomainException("Um orçamento sem itens não pode ser enviado.");

        Status = QuoteStatus.Sent;
    }

    public void Reject()
    {
        EnsureEditable();
        Status = QuoteStatus.Rejected;
    }

    /// <summary>Volta um recusado para rascunho, para ser renegociado.</summary>
    public void Reopen()
    {
        if (Status != QuoteStatus.Rejected)
            throw new DomainException("Só um orçamento recusado pode ser reaberto.");

        Status = QuoteStatus.Draft;
    }

    /// <summary>
    /// Aprova e registra o contrato gerado. O contrato em si é montado pelo caso
    /// de uso, que conhece parcelamento e forma de pagamento; aqui fica o elo e
    /// a garantia de que um orçamento vira um contrato só.
    /// </summary>
    public void Approve(Guid contractId)
    {
        EnsureCanBeApproved();
        Status = QuoteStatus.Approved;
        ContractId = contractId;
    }

    /// <summary>
    /// A checagem separada da aprovação porque o caso de uso precisa dela antes
    /// de montar o contrato: um orçamento vazio tem de falhar dizendo "sem
    /// itens", não "o valor total do contrato deve ser maior que zero".
    /// </summary>
    public void EnsureCanBeApproved()
    {
        if (Status == QuoteStatus.Approved)
            throw new DomainException("Este orçamento já foi aprovado.");

        if (Status == QuoteStatus.Rejected)
            throw new DomainException("Este orçamento foi recusado. Reabra antes de aprovar.");

        if (_items.Count == 0)
            throw new DomainException("Um orçamento sem itens não pode ser aprovado.");

        if (Total <= 0)
            throw new DomainException("O total do orçamento deve ser maior que zero.");
    }

    public void AttachToEvent(Guid? eventId)
    {
        EnsureEditable();
        EventId = eventId;
    }

    private void EnsureEditable()
    {
        if (Status == QuoteStatus.Approved)
            throw new DomainException("Este orçamento já virou contrato e não pode mais ser alterado.");

        if (Status == QuoteStatus.Rejected)
            throw new DomainException("Este orçamento foi recusado. Reabra para alterá-lo.");
    }

    private QuoteItem FindItem(Guid itemId) =>
        _items.FirstOrDefault(i => i.Id == itemId)
        ?? throw new DomainException("Item não encontrado neste orçamento.");

    private int NextPosition() => _items.Count == 0 ? 1 : _items.Max(i => i.Position) + 1;

    private void Renumber()
    {
        var ordered = _items.OrderBy(i => i.Position).ToList();
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].MoveTo(index + 1);
    }

    private void Apply(string clientName, string title, DateOnly issuedOn, DateOnly validUntil, string? notes)
    {
        if (validUntil < issuedOn)
            throw new DomainException("A validade não pode ser anterior à data de emissão.");

        ClientName = Guard.AgainstNullOrWhiteSpace(clientName, "cliente", MaxClientLength);
        Title = Guard.AgainstNullOrWhiteSpace(title, "título", MaxTitleLength);
        IssuedOn = issuedOn;
        ValidUntil = validUntil;

        Notes = string.IsNullOrWhiteSpace(notes)
            ? null
            : Guard.AgainstNullOrWhiteSpace(notes, "observações", MaxNotesLength);
    }
}
