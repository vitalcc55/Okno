using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

[JsonConverter(typeof(InputPointJsonConverter))]
public sealed record InputPoint
{
    private bool _hasValidObject = true;
    private int _x;
    private bool _hasX;
    private bool _hasValidX;
    private int _y;
    private bool _hasY;
    private bool _hasValidY;
    private IDictionary<string, JsonElement>? _additionalProperties;

    public InputPoint()
    {
    }

    public InputPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    [JsonPropertyName("x")]
    public int X
    {
        get => _x;
        init => SetX(value);
    }

    [JsonPropertyName("y")]
    public int Y
    {
        get => _y;
        init => SetY(value);
    }

    [JsonIgnore]
    public bool HasValidObject => _hasValidObject;

    [JsonIgnore]
    public bool HasX => _hasX;

    [JsonIgnore]
    public bool HasValidX => _hasValidX;

    [JsonIgnore]
    public bool HasY => _hasY;

    [JsonIgnore]
    public bool HasValidY => _hasValidY;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value;
    }

    internal void SetX(int value)
    {
        _x = value;
        _hasX = true;
        _hasValidX = true;
    }

    internal void SetY(int value)
    {
        _y = value;
        _hasY = true;
        _hasValidY = true;
    }

    internal void MarkXInvalid()
    {
        _x = default;
        _hasX = true;
        _hasValidX = false;
    }

    internal void MarkYInvalid()
    {
        _y = default;
        _hasY = true;
        _hasValidY = false;
    }

    internal void SetAdditionalProperty(string name, JsonElement value)
    {
        (_additionalProperties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[name] = value;
    }

    internal void MarkObjectInvalid()
    {
        _hasValidObject = false;
    }
}

internal sealed class InputPointJsonConverter : JsonConverter<InputPoint>
{
    public override bool HandleNull => true;

    public override InputPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            InputPoint invalidPoint = new();
            invalidPoint.MarkObjectInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return invalidPoint;
        }

        InputPoint point = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return point;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("InputPoint содержит неожиданный token.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("InputPoint завершился раньше значения свойства.");
            }

            switch (propertyName)
            {
                case "x":
                    ReadInt32(ref reader, point.SetX, point.MarkXInvalid);
                    break;
                case "y":
                    ReadInt32(ref reader, point.SetY, point.MarkYInvalid);
                    break;
                default:
                    point.SetAdditionalProperty(propertyName, InputJsonBindingHelpers.CloneValue(ref reader));
                    break;
            }
        }

        throw new JsonException("InputPoint завершился раньше конца объекта.");
    }

    public override void Write(Utf8JsonWriter writer, InputPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteInt32(writer, "x", value.HasX, value.HasValidX, value.X);
        WriteInt32(writer, "y", value.HasY, value.HasValidY, value.Y);

        if (value.AdditionalProperties is not null)
        {
            foreach ((string key, JsonElement additionalValue) in value.AdditionalProperties)
            {
                writer.WritePropertyName(key);
                additionalValue.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    private static void ReadInt32(ref Utf8JsonReader reader, Action<int> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
        {
            assign(value);
            return;
        }

        markInvalid();
        InputJsonBindingHelpers.SkipNestedValue(ref reader);
    }

    private static void WriteInt32(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, int value)
    {
        if (!isPresent)
        {
            return;
        }

        if (isValid)
        {
            writer.WriteNumber(propertyName, value);
            return;
        }

        writer.WriteNull(propertyName);
    }
}
