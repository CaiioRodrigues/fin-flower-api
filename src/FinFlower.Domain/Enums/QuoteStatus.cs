namespace FinFlower.Domain.Enums;

public enum QuoteStatus
{
    /// <summary>Em montagem. É o único estado em que os itens ainda mudam livremente.</summary>
    Draft = 1,

    /// <summary>Enviado ao cliente, aguardando resposta.</summary>
    Sent = 2,

    /// <summary>Aprovado e já convertido em contrato.</summary>
    Approved = 3,

    /// <summary>Recusado pelo cliente.</summary>
    Rejected = 4,
}
