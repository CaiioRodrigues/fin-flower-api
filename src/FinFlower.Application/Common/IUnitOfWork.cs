namespace FinFlower.Application.Common;

/// <summary>Confirma, em uma única transação, tudo que o caso de uso alterou.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
