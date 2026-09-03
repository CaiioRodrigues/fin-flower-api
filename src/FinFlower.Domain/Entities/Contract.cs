using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;

namespace FinFlower.Domain.Entities;

/// <summary>
/// O previsto: quanto foi contratado, em quantas parcelas, para quando. Raiz de
/// agregação própria — se vivesse dentro do <see cref="Event"/>, abrir um evento
/// carregaria os PDFs junto, e um contrato sem evento não teria onde morar.
/// </summary>
public sealed class Contract : AuditableEntity
{
    public const int MaxCounterpartyLength = 160;
    public const int MaxDescriptionLength = 500;
    public const int MaxInstallments = 120;

    private readonly List<Installment> _installments = [];

    private Contract() { } // EF Core

    public Contract(
        Guid ownerId,
        ContractDirection direction,
        string counterparty,
        string? description,
        decimal totalAmount,
        PaymentMethod paymentMethod,
        int installmentCount,
        DateOnly firstDueDate,
        DateOnly signedOn,
        Guid? eventId = null,
        Guid? quoteId = null)
    {
        if (ownerId == Guid.Empty) throw new DomainException("O contrato precisa de um dono.");

        if (installmentCount is < 1 or > MaxInstallments)
            throw new DomainException($"O número de parcelas deve estar entre 1 e {MaxInstallments}.");

        EventId = eventId;
        QuoteId = quoteId;
        OwnerId = ownerId;
        Direction = direction;
        Counterparty = Guard.AgainstNullOrWhiteSpace(counterparty, "contratante", MaxCounterpartyLength);
        Description = NormalizeDescription(description);
        TotalAmount = Guard.AgainstNonPositiveMoney(totalAmount, "valor total");
        PaymentMethod = paymentMethod;
        SignedOn = signedOn;

        GenerateInstallments(installmentCount, firstDueDate);
    }

    /// <summary>Evento a que o contrato se refere, quando há um.</summary>
    public Guid? EventId { get; private set; }

    /// <summary>Orçamento que originou o contrato, quando ele nasceu de uma proposta.</summary>
    public Guid? QuoteId { get; private set; }

    /// <summary>Dono do contrato: toda consulta filtra por ele.</summary>
    public Guid OwnerId { get; private set; }

    public ContractDirection Direction { get; private set; }
    public string Counterparty { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal TotalAmount { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public DateOnly SignedOn { get; private set; }

    public IReadOnlyCollection<Installment> Installments => _installments.AsReadOnly();

    public ContractAttachment? Attachment { get; private set; }

    public bool HasAttachment => Attachment is not null;

    /// <summary>Parcelas canceladas saem do total previsto.</summary>
    public decimal ActiveAmount => _installments
        .Where(i => i.Status != InstallmentStatus.Canceled)
        .Sum(i => i.Amount);

    public decimal SettledAmount => _installments
        .Where(i => i.Status == InstallmentStatus.Settled)
        .Sum(i => i.SettledAmount ?? i.Amount);

    public decimal OpenAmount => _installments.Where(i => i.IsOpen).Sum(i => i.Amount);

    public bool IsFullySettled => _installments.All(i => i.Status != InstallmentStatus.Pending);

    public void UpdateDetails(
        ContractDirection direction,
        string counterparty,
        string? description,
        PaymentMethod paymentMethod,
        DateOnly signedOn,
        Guid? eventId)
    {
        EventId = eventId;

        // O valor total e o parcelamento não mudam por aqui: alterá-los com
        // parcelas já liquidadas deixaria o contrato incoerente com o caixa.
        Direction = direction;
        Counterparty = Guard.AgainstNullOrWhiteSpace(counterparty, "contratante", MaxCounterpartyLength);
        Description = NormalizeDescription(description);
        PaymentMethod = paymentMethod;
        SignedOn = signedOn;
    }

    public Installment FindInstallment(int number) =>
        _installments.FirstOrDefault(i => i.Number == number)
        ?? throw new DomainException($"Parcela {number} não encontrada neste contrato.");

    /// <summary>
    /// Liquida uma parcela e devolve o lançamento que ela gera no caixa. O
    /// sentido do contrato decide o sentido do dinheiro: a receber entra,
    /// a pagar sai.
    /// </summary>
    public Entry SettleInstallment(
        int number,
        DateOnly settledOn,
        decimal settledAmount,
        string? description,
        string category)
    {
        var installment = FindInstallment(number);

        var entry = Entry.FromInstallment(
            OwnerId,
            installment.Id,
            Direction == ContractDirection.Receivable ? EntryType.Income : EntryType.Expense,
            description ?? $"{Counterparty} — parcela {number}/{_installments.Count}",
            settledAmount,
            category,
            settledOn,
            EventId);

        installment.Settle(settledOn, settledAmount, entry.Id);
        return entry;
    }

    /// <summary>Desfaz a liquidação e devolve o lançamento a ser removido.</summary>
    public Guid UnsettleInstallment(int number) => FindInstallment(number).Unsettle();

    public void CancelInstallment(int number) => FindInstallment(number).Cancel();

    public void RescheduleInstallment(int number, DateOnly dueDate) =>
        FindInstallment(number).Reschedule(dueDate);

    /// <summary>
    /// Altera o valor de uma parcela em aberto, redistribuindo a diferença entre
    /// as demais em aberto — a soma das parcelas continua igual ao contratado.
    /// </summary>
    public void ChangeInstallmentAmount(int number, decimal amount)
    {
        var target = FindInstallment(number);
        var others = _installments.Where(i => i.IsOpen && i.Number != number).ToList();

        if (others.Count == 0)
            throw new DomainException("Não há outra parcela em aberto para absorver a diferença.");

        var rounded = Guard.AgainstNonPositiveMoney(amount, "valor da parcela");

        // OpenAmount já inclui a parcela alvo: o que sobra para as demais é o
        // saldo em aberto menos o novo valor dela.
        var remaining = OpenAmount - rounded;

        if (remaining <= 0)
            throw new DomainException("O valor deixa as demais parcelas em aberto sem saldo.");

        target.ChangeAmount(rounded);

        var shares = MoneySplit.Into(remaining, others.Count);
        for (var index = 0; index < others.Count; index++)
            others[index].ChangeAmount(shares[index]);
    }

    public ContractAttachment AttachDocument(string fileName, byte[] content, DateTimeOffset now)
    {
        // Substitui o anterior: o contrato tem um documento, e trocá-lo é o caso
        // comum (assinatura, aditivo, versão corrigida).
        Attachment = new ContractAttachment(Id, fileName, content, now);
        return Attachment;
    }

    public void RemoveAttachment() => Attachment = null;

    private void GenerateInstallments(int count, DateOnly firstDueDate)
    {
        var shares = MoneySplit.Into(TotalAmount, count);

        for (var index = 0; index < count; index++)
        {
            _installments.Add(new Installment(
                Id,
                index + 1,
                shares[index],
                // AddMonths cuida do fim de mês: 31/01 + 1 mês vira 28/02.
                firstDueDate.AddMonths(index)));
        }
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var trimmed = description.Trim();
        if (trimmed.Length > MaxDescriptionLength)
            throw new DomainException($"A descrição deve ter no máximo {MaxDescriptionLength} caracteres.");

        return trimmed;
    }
}
