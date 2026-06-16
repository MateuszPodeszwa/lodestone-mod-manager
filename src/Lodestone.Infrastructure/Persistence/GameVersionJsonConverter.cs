using System.Text.Json;
using System.Text.Json.Serialization;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>Serializes a <see cref="GameVersion"/> as its plain string value (e.g. <c>"1.21.4"</c>).</summary>
public sealed class GameVersionJsonConverter : JsonConverter<GameVersion>
{
    public override GameVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : GameVersion.Parse(raw);
    }

    public override void Write(Utf8JsonWriter writer, GameVersion value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
