namespace FinFlower.Domain.Enums;

public enum InstallmentStatus
{
    /// <summary>Ainda não liquidada. Vencida é uma leitura da data, não um estado
    /// guardado — assim nenhuma rotina precisa varrer o banco para atualizar status.</summary>
    Pending = 1,

    /// <summary>Recebida (a receber) ou paga (a pagar).</summary>
    Settled = 2,

    /// <summary>Cancelada: sai do previsto sem ter sido liquidada.</summary>
    Canceled = 3,
}
