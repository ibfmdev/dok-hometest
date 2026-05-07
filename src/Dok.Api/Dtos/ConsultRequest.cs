using System.Text.Json.Serialization;

namespace Dok.Api.Dtos;

public sealed class ConsultRequest
{
    [JsonPropertyName("placa")]
    public required Plate Placa { get; init; }
}
