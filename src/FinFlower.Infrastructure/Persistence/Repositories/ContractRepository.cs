using FinFlower.Application.Abstractions;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class ContractRepository(AppDbContext context) : IContractRepository
{
    public Task<Contract?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        context.Contracts
            .Include(c => c.Installments)
            // Sem incluir o anexo: carregar o PDF para alterar uma parcela seria
            // trazer megabytes à toa.
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);

    public Task<Contract?> GetWithAttachmentAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        context.Contracts
            .Include(c => c.Installments)
            .Include(c => c.Attachment)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId, cancellationToken);

    public void Add(Contract contract) => context.Contracts.Add(contract);

    public void Remove(Contract contract) => context.Contracts.Remove(contract);
}
