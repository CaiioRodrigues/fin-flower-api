namespace FinFlower.Application.Common;

/// <summary>Relógio injetável: sem ele, expiração de token não é testável.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
