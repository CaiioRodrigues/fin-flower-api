using FinFlower.Domain.Entities;

namespace FinFlower.Application.Abstractions;

public interface IContractRepository
{
    /// <summary>Contrato com as parcelas, sem o conteúdo do PDF.</summary>
    Task<Contract?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Contrato com o anexo carregado — só para gravar ou baixar o arquivo.</summary>
    Task<Contract?> GetWithAttachmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default);

    void Add(Contract contract);
    void Remove(Contract contract);
}
