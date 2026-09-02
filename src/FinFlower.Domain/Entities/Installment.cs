using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;

namespace FinFlower.Domain.Entities;

/// <summary>
/// Uma parcela do contrato. Só nasce e muda através do <see cref="Contract"/>,
/// para a soma das parcelas nunca divergir do total contratado.
/// </summary>
public sealed class Installment : Entity
{
    private Installment() { } // EF Core

    internal Installment(Guid contractId, int number, decimal amount, DateOnly dueDate)
    {
        if (number < 1) throw new DomainException("O número da parcela deve ser positivo.");

        ContractId = contractId;
        Number = number;
        Amount = Guard.AgainstNonPositiveMoney(amount, "valor da parcela");
        DueDate = dueDate;
        Status = InstallmentStatus.Pending;
    }

    public Guid ContractId { get; private set; }

    /// <summary>Posição na sequência: 1 de 3, 2 de 3…</summary>
    public int Number { get; private set; }

    public decimal Amount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InstallmentStatus Status { get; private set; }

    public DateOnly? SettledOn { get; private set; }

    /// <summary>Valor efetivamente liquidado, que pode diferir do previsto por
    /// desconto ou juros.</summary>
    public decimal? SettledAmount { get; private set; }

    /// <summary>Lançamento gerado na liquidação. É o elo entre previsto e realizado.</summary>
    public Guid? EntryId { get; private set; }

    /// <summary>Vencida é calculado, não guardado: nenhuma rotina precisa varrer
    /// o banco para virar status quando o dia muda.</summary>
    public bool IsOverdue(DateOnly today) => Status == InstallmentStatus.Pending && DueDate < today;

    public bool IsOpen => Status == InstallmentStatus.Pending;

    internal void Settle(DateOnly settledOn, decimal settledAmount, Guid entryId)
    {
        if (Status == InstallmentStatus.Settled)
            throw new DomainException($"A parcela {Number} já foi liquidada.");

        if (Status == InstallmentStatus.Canceled)
            throw new DomainException($"A parcela {Number} está cancelada.");

        Status = InstallmentStatus.Settled;
        SettledOn = settledOn;
        SettledAmount = Guard.AgainstNonPositiveMoney(settledAmount, "valor liquidado");
        EntryId = entryId;
    }

    internal Guid Unsettle()
    {
        if (Status != InstallmentStatus.Settled)
            throw new DomainException($"A parcela {Number} não está liquidada.");

        var entryId = EntryId
            ?? throw new DomainException("A parcela liquidada não tem lançamento associado.");

        Status = InstallmentStatus.Pending;
        SettledOn = null;
        SettledAmount = null;
        EntryId = null;

        return entryId;
    }

    internal void Cancel()
    {
        if (Status == InstallmentStatus.Settled)
            throw new DomainException($"A parcela {Number} já foi liquidada. Estorne antes de cancelar.");

        Status = InstallmentStatus.Canceled;
    }

    internal void Reschedule(DateOnly dueDate) => DueDate = EnsureOpen(dueDate);

    internal void ChangeAmount(decimal amount) =>
        Amount = Guard.AgainstNonPositiveMoney(EnsureOpenAmount(amount), "valor da parcela");

    private DateOnly EnsureOpen(DateOnly value)
    {
        if (!IsOpen) throw new DomainException($"A parcela {Number} não está em aberto.");
        return value;
    }

    private decimal EnsureOpenAmount(decimal value)
    {
        if (!IsOpen) throw new DomainException($"A parcela {Number} não está em aberto.");
        return value;
    }
}
