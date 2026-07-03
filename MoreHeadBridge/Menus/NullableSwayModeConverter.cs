using Newtonsoft.Json;
using System;

namespace MoreHeadBridge;

/// Reads SwayMode? from both the new string format and old bool format (migration).
/// Old saves: true → Moderate, false → None.
internal sealed class NullableSwayModeConverter : JsonConverter<SwayMode?>
{
    public override SwayMode? ReadJson(JsonReader reader, Type objectType, SwayMode? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        if (reader.TokenType == JsonToken.Boolean)
            return (bool)reader.Value! ? SwayMode.Moderate : SwayMode.None;
        if (reader.TokenType == JsonToken.String &&
            Enum.TryParse<SwayMode>((string)reader.Value!, ignoreCase: true, out var v))
            return v;
        return null;
    }

    public override void WriteJson(JsonWriter writer, SwayMode? value, JsonSerializer serializer)
    {
        if (value == null) writer.WriteNull();
        else writer.WriteValue(value.ToString());
    }
}
