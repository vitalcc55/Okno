using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

[JsonConverter(typeof(InputCaptureReferenceJsonConverter))]
public sealed record InputCaptureReference
{
    private bool _hasValidObject = true;
    private InputBounds? _bounds;
    private bool _hasBounds;
    private bool _hasValidBounds;
    private int _pixelWidth;
    private bool _hasPixelWidth;
    private bool _hasValidPixelWidth;
    private int _pixelHeight;
    private bool _hasPixelHeight;
    private bool _hasValidPixelHeight;
    private int? _effectiveDpi;
    private bool _hasEffectiveDpi;
    private bool _hasValidEffectiveDpi;
    private DateTimeOffset? _capturedAtUtc;
    private bool _hasCapturedAtUtc;
    private bool _hasValidCapturedAtUtc;
    private InputBounds? _frameBounds;
    private bool _hasFrameBounds;
    private bool _hasValidFrameBounds;
    private InputTargetIdentity? _targetIdentity;
    private bool _hasTargetIdentity;
    private bool _hasValidTargetIdentity;
    private IDictionary<string, JsonElement>? _additionalProperties;

    public InputCaptureReference()
    {
    }

    public InputCaptureReference(
        InputBounds bounds,
        int pixelWidth,
        int pixelHeight,
        int? effectiveDpi = null,
        DateTimeOffset? capturedAtUtc = null,
        InputBounds? frameBounds = null,
        InputTargetIdentity? targetIdentity = null)
    {
        Bounds = bounds;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        EffectiveDpi = effectiveDpi;
        CapturedAtUtc = capturedAtUtc;
        if (frameBounds is not null)
        {
            FrameBounds = frameBounds;
        }

        if (targetIdentity is not null)
        {
            TargetIdentity = targetIdentity;
        }
    }

    [JsonPropertyName("bounds")]
    public InputBounds? Bounds
    {
        get => _bounds;
        init => SetBounds(value);
    }

    [JsonPropertyName("pixelWidth")]
    public int PixelWidth
    {
        get => _pixelWidth;
        init => SetPixelWidth(value);
    }

    [JsonPropertyName("pixelHeight")]
    public int PixelHeight
    {
        get => _pixelHeight;
        init => SetPixelHeight(value);
    }

    [JsonPropertyName("effectiveDpi")]
    public int? EffectiveDpi
    {
        get => _effectiveDpi;
        init => SetEffectiveDpi(value);
    }

    [JsonPropertyName("capturedAtUtc")]
    public DateTimeOffset? CapturedAtUtc
    {
        get => _capturedAtUtc;
        init => SetCapturedAtUtc(value);
    }

    [JsonPropertyName("frameBounds")]
    public InputBounds? FrameBounds
    {
        get => _frameBounds;
        init => SetFrameBounds(value);
    }

    [JsonPropertyName("targetIdentity")]
    public InputTargetIdentity? TargetIdentity
    {
        get => _targetIdentity;
        init => SetTargetIdentity(value);
    }

    [JsonIgnore]
    public bool HasValidObject => _hasValidObject;

    [JsonIgnore]
    public bool HasBounds => _hasBounds;

    [JsonIgnore]
    public bool HasValidBounds => _hasValidBounds;

    [JsonIgnore]
    public bool HasPixelWidth => _hasPixelWidth;

    [JsonIgnore]
    public bool HasValidPixelWidth => _hasValidPixelWidth;

    [JsonIgnore]
    public bool HasPixelHeight => _hasPixelHeight;

    [JsonIgnore]
    public bool HasValidPixelHeight => _hasValidPixelHeight;

    [JsonIgnore]
    public bool HasEffectiveDpi => _hasEffectiveDpi;

    [JsonIgnore]
    public bool HasValidEffectiveDpi => _hasValidEffectiveDpi;

    [JsonIgnore]
    public bool HasCapturedAtUtc => _hasCapturedAtUtc;

    [JsonIgnore]
    public bool HasValidCapturedAtUtc => _hasValidCapturedAtUtc;

    [JsonIgnore]
    public bool HasFrameBounds => _hasFrameBounds;

    [JsonIgnore]
    public bool HasValidFrameBounds => _hasValidFrameBounds;

    [JsonIgnore]
    public bool HasTargetIdentity => _hasTargetIdentity;

    [JsonIgnore]
    public bool HasValidTargetIdentity => _hasValidTargetIdentity;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value;
    }

    internal void SetBounds(InputBounds? value)
    {
        _bounds = value;
        _hasBounds = true;
        _hasValidBounds = value is not null;
    }

    internal void MarkBoundsInvalid()
    {
        _bounds = null;
        _hasBounds = true;
        _hasValidBounds = false;
    }

    internal void SetPixelWidth(int value)
    {
        _pixelWidth = value;
        _hasPixelWidth = true;
        _hasValidPixelWidth = true;
    }

    internal void MarkPixelWidthInvalid()
    {
        _pixelWidth = default;
        _hasPixelWidth = true;
        _hasValidPixelWidth = false;
    }

    internal void SetPixelHeight(int value)
    {
        _pixelHeight = value;
        _hasPixelHeight = true;
        _hasValidPixelHeight = true;
    }

    internal void MarkPixelHeightInvalid()
    {
        _pixelHeight = default;
        _hasPixelHeight = true;
        _hasValidPixelHeight = false;
    }

    internal void SetEffectiveDpi(int? value)
    {
        _effectiveDpi = value;
        _hasEffectiveDpi = true;
        _hasValidEffectiveDpi = true;
    }

    internal void MarkEffectiveDpiInvalid()
    {
        _effectiveDpi = null;
        _hasEffectiveDpi = true;
        _hasValidEffectiveDpi = false;
    }

    internal void SetCapturedAtUtc(DateTimeOffset? value)
    {
        _capturedAtUtc = value;
        _hasCapturedAtUtc = true;
        _hasValidCapturedAtUtc = true;
    }

    internal void MarkCapturedAtUtcInvalid()
    {
        _capturedAtUtc = null;
        _hasCapturedAtUtc = true;
        _hasValidCapturedAtUtc = false;
    }

    internal void SetFrameBounds(InputBounds? value)
    {
        _frameBounds = value;
        _hasFrameBounds = true;
        _hasValidFrameBounds = value is not null;
    }

    internal void MarkFrameBoundsInvalid()
    {
        _frameBounds = null;
        _hasFrameBounds = true;
        _hasValidFrameBounds = false;
    }

    internal void SetTargetIdentity(InputTargetIdentity? value)
    {
        _targetIdentity = value;
        _hasTargetIdentity = true;
        _hasValidTargetIdentity = value is not null;
    }

    internal void MarkTargetIdentityInvalid()
    {
        _targetIdentity = null;
        _hasTargetIdentity = true;
        _hasValidTargetIdentity = false;
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

internal sealed class InputCaptureReferenceJsonConverter : JsonConverter<InputCaptureReference>
{
    public override bool HandleNull => true;

    public override InputCaptureReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            InputCaptureReference invalidCaptureReference = new();
            invalidCaptureReference.MarkObjectInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return invalidCaptureReference;
        }

        InputCaptureReference captureReference = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return captureReference;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("InputCaptureReference содержит неожиданный token.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("InputCaptureReference завершился раньше значения свойства.");
            }

            switch (propertyName)
            {
                case "bounds":
                    ReadBounds(ref reader, options, captureReference);
                    break;
                case "pixelWidth":
                    ReadInt32(ref reader, captureReference.SetPixelWidth, captureReference.MarkPixelWidthInvalid);
                    break;
                case "pixelHeight":
                    ReadInt32(ref reader, captureReference.SetPixelHeight, captureReference.MarkPixelHeightInvalid);
                    break;
                case "effectiveDpi":
                    ReadNullableInt32(ref reader, captureReference.SetEffectiveDpi, captureReference.MarkEffectiveDpiInvalid);
                    break;
                case "capturedAtUtc":
                    ReadNullableDateTimeOffset(ref reader, captureReference.SetCapturedAtUtc, captureReference.MarkCapturedAtUtcInvalid);
                    break;
                case "frameBounds":
                    ReadFrameBounds(ref reader, options, captureReference);
                    break;
                case "targetIdentity":
                    ReadTargetIdentity(ref reader, options, captureReference);
                    break;
                default:
                    captureReference.SetAdditionalProperty(propertyName, InputJsonBindingHelpers.CloneValue(ref reader));
                    break;
            }
        }

        throw new JsonException("InputCaptureReference завершился раньше конца объекта.");
    }

    public override void Write(Utf8JsonWriter writer, InputCaptureReference value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.HasBounds)
        {
            writer.WritePropertyName("bounds");
            if (value.HasValidBounds && value.Bounds is not null)
            {
                JsonSerializer.Serialize(writer, value.Bounds, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        WriteInt32(writer, "pixelWidth", value.HasPixelWidth, value.HasValidPixelWidth, value.PixelWidth);
        WriteInt32(writer, "pixelHeight", value.HasPixelHeight, value.HasValidPixelHeight, value.PixelHeight);
        WriteNullableInt32(writer, "effectiveDpi", value.HasEffectiveDpi, value.HasValidEffectiveDpi, value.EffectiveDpi);
        WriteNullableDateTimeOffset(writer, "capturedAtUtc", value.HasCapturedAtUtc, value.HasValidCapturedAtUtc, value.CapturedAtUtc);
        if (value.HasFrameBounds)
        {
            writer.WritePropertyName("frameBounds");
            if (value.HasValidFrameBounds && value.FrameBounds is not null)
            {
                JsonSerializer.Serialize(writer, value.FrameBounds, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        if (value.HasTargetIdentity)
        {
            writer.WritePropertyName("targetIdentity");
            if (value.HasValidTargetIdentity && value.TargetIdentity is not null)
            {
                JsonSerializer.Serialize(writer, value.TargetIdentity, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

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

    private static void ReadBounds(ref Utf8JsonReader reader, JsonSerializerOptions options, InputCaptureReference captureReference)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            captureReference.MarkBoundsInvalid();
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            captureReference.MarkBoundsInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return;
        }

        InputBounds? bounds = JsonSerializer.Deserialize<InputBounds>(ref reader, options);
        captureReference.SetBounds(bounds);
    }

    private static void ReadFrameBounds(ref Utf8JsonReader reader, JsonSerializerOptions options, InputCaptureReference captureReference)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            captureReference.MarkFrameBoundsInvalid();
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            captureReference.MarkFrameBoundsInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return;
        }

        InputBounds? bounds = JsonSerializer.Deserialize<InputBounds>(ref reader, options);
        captureReference.SetFrameBounds(bounds);
    }

    private static void ReadTargetIdentity(ref Utf8JsonReader reader, JsonSerializerOptions options, InputCaptureReference captureReference)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            captureReference.MarkTargetIdentityInvalid();
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            captureReference.MarkTargetIdentityInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return;
        }

        InputTargetIdentity? targetIdentity = JsonSerializer.Deserialize<InputTargetIdentity>(ref reader, options);
        captureReference.SetTargetIdentity(targetIdentity);
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

    private static void ReadNullableInt32(ref Utf8JsonReader reader, Action<int?> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            assign(null);
            return;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
        {
            assign(value);
            return;
        }

        markInvalid();
        InputJsonBindingHelpers.SkipNestedValue(ref reader);
    }

    private static void ReadNullableDateTimeOffset(ref Utf8JsonReader reader, Action<DateTimeOffset?> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            assign(null);
            return;
        }

        if (reader.TokenType == JsonTokenType.String
            && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset value))
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

    private static void WriteNullableInt32(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, int? value)
    {
        if (!isPresent)
        {
            return;
        }

        if (!isValid || value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteNumber(propertyName, value.Value);
    }

    private static void WriteNullableDateTimeOffset(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, DateTimeOffset? value)
    {
        if (!isPresent)
        {
            return;
        }

        if (!isValid || value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value.Value);
    }
}
