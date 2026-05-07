using System.Text.Json.Serialization;

namespace Dok.Api.Dtos;

public sealed record ErrorPayload(
    [property: JsonPropertyName("error")] string Error);

public sealed record UnknownDebtTypeErrorPayload(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("type")] string Type);
