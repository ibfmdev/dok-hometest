using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dok.Api.Json;

public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString("F2", CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for Money"),
        };
        return Money.Of(decimal.Parse(text!, CultureInfo.InvariantCulture));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToJsonString());
}
