// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

[JsonConverter(typeof(InputActionJsonConverter))]
public sealed record InputAction
{
    private bool _hasValidObject = true;
    private string _type = string.Empty;
    private bool _hasType;
    private bool _hasValidType = true;
    private InputPoint? _point;
    private bool _hasPoint;
    private IReadOnlyList<InputPoint>? _path;
    private bool _hasPath;
    private bool _hasValidPath = true;
    private string? _coordinateSpace;
    private bool _hasCoordinateSpace;
    private bool _hasValidCoordinateSpace = true;
    private string? _button;
    private bool _hasButton;
    private bool _hasValidButton = true;
    private IReadOnlyList<string>? _keys;
    private bool _hasKeys;
    private bool _hasValidKeys = true;
    private string? _text;
    private bool _hasText;
    private bool _hasValidText = true;
    private string? _key;
    private bool _hasKey;
    private bool _hasValidKey = true;
    private int? _repeat;
    private bool _hasRepeat;
    private bool _hasValidRepeat = true;
    private int? _delta;
    private bool _hasDelta;
    private bool _hasValidDelta = true;
    private string? _direction;
    private bool _hasDirection;
    private bool _hasValidDirection = true;
    private InputCaptureReference? _captureReference;
    private bool _hasCaptureReference;
    private IDictionary<string, JsonElement>? _additionalProperties;

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        init => SetType(value);
    }

    [JsonPropertyName("point")]
    public InputPoint? Point
    {
        get => _point;
        init => SetPoint(value);
    }

    [JsonPropertyName("path")]
    public IReadOnlyList<InputPoint>? Path
    {
        get => _path;
        init => SetPath(value);
    }

    [JsonPropertyName("coordinateSpace")]
    public string? CoordinateSpace
    {
        get => _coordinateSpace;
        init => SetCoordinateSpace(value);
    }

    [JsonPropertyName("button")]
    public string? Button
    {
        get => _button;
        init => SetButton(value);
    }

    [JsonPropertyName("keys")]
    public IReadOnlyList<string>? Keys
    {
        get => _keys;
        init => SetKeys(value);
    }

    [JsonPropertyName("text")]
    public string? Text
    {
        get => _text;
        init => SetText(value);
    }

    [JsonPropertyName("key")]
    public string? Key
    {
        get => _key;
        init => SetKey(value);
    }

    [JsonPropertyName("repeat")]
    public int? Repeat
    {
        get => _repeat;
        init => SetRepeat(value);
    }

    [JsonPropertyName("delta")]
    public int? Delta
    {
        get => _delta;
        init => SetDelta(value);
    }

    [JsonPropertyName("direction")]
    public string? Direction
    {
        get => _direction;
        init => SetDirection(value);
    }

    [JsonPropertyName("captureReference")]
    public InputCaptureReference? CaptureReference
    {
        get => _captureReference;
        init => SetCaptureReference(value);
    }

    [JsonIgnore]
    public bool HasValidObject => _hasValidObject;

    [JsonIgnore]
    public bool HasType => _hasType;

    [JsonIgnore]
    public bool HasValidType => _hasValidType;

    [JsonIgnore]
    public bool HasPoint => _hasPoint;

    [JsonIgnore]
    public bool HasPath => _hasPath;

    [JsonIgnore]
    public bool HasValidPath => _hasValidPath;

    [JsonIgnore]
    public bool HasCoordinateSpace => _hasCoordinateSpace;

    [JsonIgnore]
    public bool HasValidCoordinateSpace => _hasValidCoordinateSpace;

    [JsonIgnore]
    public bool HasButton => _hasButton;

    [JsonIgnore]
    public bool HasValidButton => _hasValidButton;

    [JsonIgnore]
    public bool HasKeys => _hasKeys;

    [JsonIgnore]
    public bool HasValidKeys => _hasValidKeys;

    [JsonIgnore]
    public bool HasText => _hasText;

    [JsonIgnore]
    public bool HasValidText => _hasValidText;

    [JsonIgnore]
    public bool HasKey => _hasKey;

    [JsonIgnore]
    public bool HasValidKey => _hasValidKey;

    [JsonIgnore]
    public bool HasRepeat => _hasRepeat;

    [JsonIgnore]
    public bool HasValidRepeat => _hasValidRepeat;

    [JsonIgnore]
    public bool HasDelta => _hasDelta;

    [JsonIgnore]
    public bool HasValidDelta => _hasValidDelta;

    [JsonIgnore]
    public bool HasDirection => _hasDirection;

    [JsonIgnore]
    public bool HasValidDirection => _hasValidDirection;

    [JsonIgnore]
    public bool HasCaptureReference => _hasCaptureReference;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value;
    }

    internal static InputAction CreateInvalidObject()
    {
        InputAction action = new();
        action.MarkObjectInvalid();
        return action;
    }

    internal void MarkObjectInvalid()
    {
        _hasValidObject = false;
    }

    internal void SetType(string? value)
    {
        _type = NormalizeRequiredToken(value);
        _hasType = true;
        _hasValidType = true;
    }

    internal void MarkTypeInvalid()
    {
        _type = string.Empty;
        _hasType = true;
        _hasValidType = false;
    }

    internal void SetPoint(InputPoint? value)
    {
        _point = value;
        _hasPoint = true;
    }

    internal void SetPath(IReadOnlyList<InputPoint>? value)
    {
        _path = value;
        _hasPath = true;
        _hasValidPath = true;
    }

    internal void MarkPathInvalid()
    {
        _path = null;
        _hasPath = true;
        _hasValidPath = false;
    }

    internal void SetCoordinateSpace(string? value)
    {
        _coordinateSpace = NormalizeOptionalToken(value);
        _hasCoordinateSpace = true;
        _hasValidCoordinateSpace = true;
    }

    internal void MarkCoordinateSpaceInvalid()
    {
        _coordinateSpace = null;
        _hasCoordinateSpace = true;
        _hasValidCoordinateSpace = false;
    }

    internal void SetButton(string? value)
    {
        _button = NormalizeOptionalToken(value);
        _hasButton = true;
        _hasValidButton = true;
    }

    internal void MarkButtonInvalid()
    {
        _button = null;
        _hasButton = true;
        _hasValidButton = false;
    }

    internal void SetKeys(IReadOnlyList<string>? value)
    {
        _keys = value;
        _hasKeys = true;
        _hasValidKeys = true;
    }

    internal void MarkKeysInvalid(IReadOnlyList<string>? value = null)
    {
        _keys = value;
        _hasKeys = true;
        _hasValidKeys = false;
    }

    internal void SetText(string? value)
    {
        _text = value;
        _hasText = true;
        _hasValidText = true;
    }

    internal void MarkTextInvalid()
    {
        _text = null;
        _hasText = true;
        _hasValidText = false;
    }

    internal void SetKey(string? value)
    {
        _key = NormalizeOptionalToken(value);
        _hasKey = true;
        _hasValidKey = true;
    }

    internal void MarkKeyInvalid()
    {
        _key = null;
        _hasKey = true;
        _hasValidKey = false;
    }

    internal void SetRepeat(int? value)
    {
        _repeat = value;
        _hasRepeat = true;
        _hasValidRepeat = true;
    }

    internal void MarkRepeatInvalid()
    {
        _repeat = null;
        _hasRepeat = true;
        _hasValidRepeat = false;
    }

    internal void SetDelta(int? value)
    {
        _delta = value;
        _hasDelta = true;
        _hasValidDelta = true;
    }

    internal void MarkDeltaInvalid()
    {
        _delta = null;
        _hasDelta = true;
        _hasValidDelta = false;
    }

    internal void SetDirection(string? value)
    {
        _direction = NormalizeOptionalToken(value);
        _hasDirection = true;
        _hasValidDirection = true;
    }

    internal void MarkDirectionInvalid()
    {
        _direction = null;
        _hasDirection = true;
        _hasValidDirection = false;
    }

    internal void SetCaptureReference(InputCaptureReference? value)
    {
        _captureReference = value;
        _hasCaptureReference = true;
    }

    internal void SetAdditionalProperty(string name, JsonElement value)
    {
        (_additionalProperties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[name] = value;
    }

    private static string NormalizeRequiredToken(string? value) =>
        (value ?? string.Empty).Trim();

    private static string? NormalizeOptionalToken(string? value) =>
        value?.Trim();
}

internal sealed class InputActionJsonConverter : JsonConverter<InputAction>
{
    public override bool HandleNull => true;

    public override InputAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            InputAction invalidAction = InputAction.CreateInvalidObject();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return invalidAction;
        }

        InputAction action = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return action;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("InputAction содержит неожиданный token.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("InputAction завершился раньше значения свойства.");
            }

            switch (propertyName)
            {
                case "type":
                    ReadString(ref reader, action.SetType, action.MarkTypeInvalid);
                    break;
                case "point":
                    ReadPoint(ref reader, options, action);
                    break;
                case "path":
                    ReadPath(ref reader, options, action);
                    break;
                case "coordinateSpace":
                    ReadString(ref reader, action.SetCoordinateSpace, action.MarkCoordinateSpaceInvalid);
                    break;
                case "button":
                    ReadString(ref reader, action.SetButton, action.MarkButtonInvalid);
                    break;
                case "keys":
                    ReadKeys(ref reader, action);
                    break;
                case "text":
                    ReadString(ref reader, action.SetText, action.MarkTextInvalid);
                    break;
                case "key":
                    ReadString(ref reader, action.SetKey, action.MarkKeyInvalid);
                    break;
                case "repeat":
                    ReadNullableInt32(ref reader, action.SetRepeat, action.MarkRepeatInvalid);
                    break;
                case "delta":
                    ReadNullableInt32(ref reader, action.SetDelta, action.MarkDeltaInvalid);
                    break;
                case "direction":
                    ReadString(ref reader, action.SetDirection, action.MarkDirectionInvalid);
                    break;
                case "captureReference":
                    ReadCaptureReference(ref reader, options, action);
                    break;
                default:
                    action.SetAdditionalProperty(propertyName, InputJsonBindingHelpers.CloneValue(ref reader));
                    break;
            }
        }

        throw new JsonException("InputAction завершился раньше конца объекта.");
    }

    public override void Write(Utf8JsonWriter writer, InputAction value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteString(writer, "type", value.HasType, value.HasValidType, value.Type);
        WriteObject(writer, "point", value.HasPoint, value.Point, options);
        WriteObject(writer, "path", value.HasPath, value.HasValidPath, value.Path, options);
        WriteString(writer, "coordinateSpace", value.HasCoordinateSpace, value.HasValidCoordinateSpace, value.CoordinateSpace);
        WriteString(writer, "button", value.HasButton, value.HasValidButton, value.Button);
        WriteObject(writer, "keys", value.HasKeys, value.HasValidKeys, value.Keys, options);
        WriteString(writer, "text", value.HasText, value.HasValidText, value.Text);
        WriteString(writer, "key", value.HasKey, value.HasValidKey, value.Key);
        WriteNullableInt32(writer, "repeat", value.HasRepeat, value.HasValidRepeat, value.Repeat);
        WriteNullableInt32(writer, "delta", value.HasDelta, value.HasValidDelta, value.Delta);
        WriteString(writer, "direction", value.HasDirection, value.HasValidDirection, value.Direction);
        WriteObject(writer, "captureReference", value.HasCaptureReference, value.CaptureReference, options);

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

    private static void ReadString(ref Utf8JsonReader reader, Action<string?> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            assign(reader.GetString());
            return;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            assign(null);
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

    private static void ReadPoint(ref Utf8JsonReader reader, JsonSerializerOptions options, InputAction action)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            action.SetPoint(null);
            return;
        }

        InputPoint? point = JsonSerializer.Deserialize<InputPoint>(ref reader, options);
        action.SetPoint(point);
    }

    private static void ReadPath(ref Utf8JsonReader reader, JsonSerializerOptions options, InputAction action)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            action.SetPath(null);
            return;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            action.MarkPathInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return;
        }

        List<InputPoint> path = [];
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                action.SetPath(path);
                return;
            }

            InputPoint? point = JsonSerializer.Deserialize<InputPoint>(ref reader, options);
            path.Add(point ?? new InputPoint());
        }

        throw new JsonException("InputAction.path завершился раньше конца массива.");
    }

    private static void ReadKeys(ref Utf8JsonReader reader, InputAction action)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            action.SetKeys(null);
            return;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            action.MarkKeysInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return;
        }

        List<string> keys = [];
        bool isValid = true;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                if (isValid)
                {
                    action.SetKeys(keys);
                }
                else
                {
                    action.MarkKeysInvalid(keys);
                }

                return;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                keys.Add(reader.GetString() ?? string.Empty);
                continue;
            }

            isValid = false;
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
        }

        throw new JsonException("InputAction.keys завершился раньше конца массива.");
    }

    private static void ReadCaptureReference(ref Utf8JsonReader reader, JsonSerializerOptions options, InputAction action)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            action.SetCaptureReference(null);
            return;
        }

        InputCaptureReference? captureReference = JsonSerializer.Deserialize<InputCaptureReference>(ref reader, options);
        action.SetCaptureReference(captureReference);
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, string? value)
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

        writer.WriteString(propertyName, value);
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

    private static void WriteObject<T>(Utf8JsonWriter writer, string propertyName, bool isPresent, T? value, JsonSerializerOptions options)
    {
        if (!isPresent)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }

    private static void WriteObject<T>(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, T? value, JsonSerializerOptions options)
    {
        if (!isPresent)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        if (!isValid || value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }
}
