using FinFlower.Domain.Common;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Domain.Entities;

/// <summary>
/// O dinheiro que já existia quando o sistema começou a ser usado.
///
/// Sem isto o saldo do caixa é a soma do que foi digitado, e quem começa a usar
/// o sistema em setembro vê "saldo" onde na verdade está lendo "variação desde
/// setembro" — e a projeção herda o erro inteiro.
///
/// A data é um marco, não um detalhe: ela diz onde a história registrada
/// começa. Lançamento anterior a ela não entra no saldo, porque o valor aqui já
/// o contém — contar os dois seria contar o mesmo dinheiro duas vezes.
/// </summary>
public sealed class CashOpening : AuditableEntity
{
    public const int MaxNotesLength = 300;

    private CashOpening() { } // EF Core

    public CashOpening(Guid ownerId, decimal amount, DateOnly occurredOn, string? notes = null)
    {
        if (ownerId == Guid.Empty) throw new DomainException("O saldo inicial precisa de um dono.");

        OwnerId = ownerId;
        Change(amount, occurredOn, notes);
    }

    public Guid OwnerId { get; private set; }

    /// <summary>
    /// O saldo na data. Diferente do resto do sistema, aceita valor negativo:
    /// começar no vermelho é uma situação real, e arredondar para zero mentiria
    /// justamente para quem mais precisa enxergar o buraco.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>Dia a que o saldo se refere, no começo dele.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>De onde veio o número — "extrato do Itaú", "conferido na mão".</summary>
    public string? Notes { get; private set; }

    /// <summary>A competência em que o saldo entra na linha do tempo do caixa.</summary>
    public YearMonth Competence => YearMonth.From(OccurredOn);

    public void Change(decimal amount, DateOnly occurredOn, string? notes)
    {
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        OccurredOn = occurredOn;
        Notes = string.IsNullOrWhiteSpace(notes)
            ? null
            : Guard.AgainstNullOrWhiteSpace(notes, "observação", MaxNotesLength);
    }
}
