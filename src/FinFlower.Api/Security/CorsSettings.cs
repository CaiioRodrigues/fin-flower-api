namespace FinFlower.Api.Security;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>Origens liberadas fora de desenvolvimento.</summary>
    public string[] AllowedOrigins { get; init; } = [];
}
