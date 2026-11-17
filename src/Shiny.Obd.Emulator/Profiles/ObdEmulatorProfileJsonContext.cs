using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiny.Obd.Emulator;

// Source generated so loading a profile stays AOT and trim safe. Unmapped members are an error: the
// schema disallows them too, and a misspelt property silently doing nothing is the worst outcome.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    Converters = [typeof(DrivingActionJsonConverter)]
)]
[JsonSerializable(typeof(ProfileDocument))]
internal partial class ObdEmulatorProfileJsonContext : JsonSerializerContext;


/// <summary>Driving actions as camelCase strings - "harshBrake" - matching the schema's enum.</summary>
sealed class DrivingActionJsonConverter() : JsonStringEnumConverter<DrivingAction>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);


/// <summary>A byte as two hex digits, the way PIDs are written everywhere else in OBD.</summary>
sealed class HexByteJsonConverter : JsonConverter<byte>
{
    public override byte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ParseHex(ref reader);

    public override void Write(Utf8JsonWriter writer, byte value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("X2", CultureInfo.InvariantCulture));

    internal static byte ParseHex(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected a PID as two hex digits (e.g. \"0D\") but found {reader.TokenType}");

        var text = reader.GetString();
        if (text is not { Length: 2 } || !Byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new JsonException($"'{text}' is not a PID - expected two hex digits, e.g. \"0D\"");

        return value;
    }
}


/// <summary>A PID list (<see cref="EmulatedVehicle.UnsupportedPids"/>) as hex strings.</summary>
sealed class HexByteListJsonConverter : JsonConverter<List<byte>>
{
    public override List<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected an array of PIDs but found {reader.TokenType}");

        var list = new List<byte>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(HexByteJsonConverter.ParseHex(ref reader));

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<byte> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var pid in value)
            writer.WriteStringValue(pid.ToString("X2", CultureInfo.InvariantCulture));
        writer.WriteEndArray();
    }
}
