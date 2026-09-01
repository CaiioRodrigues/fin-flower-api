namespace FinFlower.Domain.Enums;

public enum EventStatus
{
    /// <summary>Aceita novos lançamentos.</summary>
    Open = 1,

    /// <summary>Fechado: o resultado está consolidado e não aceita alterações.</summary>
    Closed = 2,
}
