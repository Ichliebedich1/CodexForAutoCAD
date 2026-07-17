using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.AutoCAD.AppServer.Protocol;

/// <summary>JSON-RPC request identifier. Codex accepts integer and string identifiers.</summary>
[JsonConverter(typeof(JsonRpcIdJsonConverter))]
public readonly struct JsonRpcId : IEquatable<JsonRpcId>
{
    private readonly long _number;
    private readonly string? _text;

    public JsonRpcId(long value)
    {
        _number = value;
        _text = null;
    }

    public JsonRpcId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _number = default;
        _text = value;
    }

    public bool IsString => _text is not null;

    public long Number => !IsString
        ? _number
        : throw new InvalidOperationException("This JSON-RPC id is a string.");

    public string Text => IsString
        ? _text!
        : throw new InvalidOperationException("This JSON-RPC id is an integer.");

    public bool Equals(JsonRpcId other)
        => _number == other._number && string.Equals(_text, other._text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is JsonRpcId other && Equals(other);

    public override int GetHashCode() => IsString
        ? StringComparer.Ordinal.GetHashCode(_text!)
        : _number.GetHashCode();

    public override string ToString() => IsString ? _text! : _number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator ==(JsonRpcId left, JsonRpcId right) => left.Equals(right);

    public static bool operator !=(JsonRpcId left, JsonRpcId right) => !left.Equals(right);

    internal static bool TryRead(JsonElement element, out JsonRpcId id)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            id = new JsonRpcId(element.GetString()!);
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            id = new JsonRpcId(number);
            return true;
        }

        id = default;
        return false;
    }
}

internal sealed class JsonRpcIdJsonConverter : JsonConverter<JsonRpcId>
{
    public override JsonRpcId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => new JsonRpcId(reader.GetString()!),
            JsonTokenType.Number when reader.TryGetInt64(out var number) => new JsonRpcId(number),
            _ => throw new JsonException("JSON-RPC id must be an integer or string."),
        };

    public override void Write(Utf8JsonWriter writer, JsonRpcId value, JsonSerializerOptions options)
    {
        if (value.IsString)
        {
            writer.WriteStringValue(value.Text);
        }
        else
        {
            writer.WriteNumberValue(value.Number);
        }
    }
}
