namespace FinFlower.Domain.Common;

public abstract class Entity
{
    // Version 7 é sequencial no tempo: evita fragmentação de índice no SQL Server,
    // problema clássico do Guid.NewGuid() como chave primária clusterizada.
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && GetType() == other.GetType() && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}

/// <summary>
/// Entidade com rastro de auditoria e exclusão lógica: nada some do banco sem registro.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkAsDeleted(DateTimeOffset now)
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = now;
    }
}
