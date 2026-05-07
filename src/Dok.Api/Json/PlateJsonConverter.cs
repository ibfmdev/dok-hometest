using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dok.Api.Json;

public sealed class PlateJsonConverter : JsonConverter<Plate>
{
    public override Plate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Plate.Parse(reader.GetString());

    public override void Write(Utf8JsonWriter writer, Plate value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
