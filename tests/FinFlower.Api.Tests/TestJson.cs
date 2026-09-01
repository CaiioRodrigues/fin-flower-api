using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinFlower.Api.Tests;

/// <summary>
/// Mesmas opções de JSON que a API usa. Sem isto o cliente de teste tentaria ler
/// os enums como número e não enxergaria o contrato que o front realmente recebe.
/// </summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
