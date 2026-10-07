using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoneStore.Api.DTOs.Common;

public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetDateTime(out var value) || value.Kind != DateTimeKind.Utc)
            throw new JsonException("Timestamp must be ISO 8601 UTC ending in Z.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        if (value.Kind != DateTimeKind.Utc) throw new JsonException("Timestamp must have UTC kind.");
        writer.WriteStringValue(value);
    }
}
