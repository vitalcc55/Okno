// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

[JsonConverter(typeof(InputBoundsJsonConverter))]
public sealed record InputBounds
{
    private bool _hasValidObject = true;
    private int _left;
    private bool _hasLeft;
    private bool _hasValidLeft;
    private int _top;
    private bool _hasTop;
    private bool _hasValidTop;
    private int _right;
    private bool _hasRight;
    private bool _hasValidRight;
    private int _bottom;
    private bool _hasBottom;
    private bool _hasValidBottom;
    private IDictionary<string, JsonElement>? _additionalProperties;

    public InputBounds()
    {
    }

    public InputBounds(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    [JsonPropertyName("left")]
    public int Left
    {
        get => _left;
        init => SetLeft(value);
    }

    [JsonPropertyName("top")]
    public int Top
    {
        get => _top;
        init => SetTop(value);
    }

    [JsonPropertyName("right")]
    public int Right
    {
        get => _right;
        init => SetRight(value);
    }

    [JsonPropertyName("bottom")]
    public int Bottom
    {
        get => _bottom;
        init => SetBottom(value);
    }

    [JsonIgnore]
    public bool HasValidObject => _hasValidObject;

    [JsonIgnore]
    public bool HasLeft => _hasLeft;

    [JsonIgnore]
    public bool HasValidLeft => _hasValidLeft;

    [JsonIgnore]
    public bool HasTop => _hasTop;

    [JsonIgnore]
    public bool HasValidTop => _hasValidTop;

    [JsonIgnore]
    public bool HasRight => _hasRight;

    [JsonIgnore]
    public bool HasValidRight => _hasValidRight;

    [JsonIgnore]
    public bool HasBottom => _hasBottom;

    [JsonIgnore]
    public bool HasValidBottom => _hasValidBottom;

    [JsonIgnore]
    public int Width => Right - Left;

    [JsonIgnore]
    public int Height => Bottom - Top;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value;
    }

    internal void SetLeft(int value)
    {
        _left = value;
        _hasLeft = true;
        _hasValidLeft = true;
    }

    internal void SetTop(int value)
    {
        _top = value;
        _hasTop = true;
        _hasValidTop = true;
    }

    internal void SetRight(int value)
    {
        _right = value;
        _hasRight = true;
        _hasValidRight = true;
    }

    internal void SetBottom(int value)
    {
        _bottom = value;
        _hasBottom = true;
        _hasValidBottom = true;
    }

    internal void MarkLeftInvalid()
    {
        _hasLeft = true;
        _hasValidLeft = false;
        _left = default;
    }

    internal void MarkTopInvalid()
    {
        _hasTop = true;
        _hasValidTop = false;
        _top = default;
    }

    internal void MarkRightInvalid()
    {
        _hasRight = true;
        _hasValidRight = false;
        _right = default;
    }

    internal void MarkBottomInvalid()
    {
        _hasBottom = true;
        _hasValidBottom = false;
        _bottom = default;
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

internal sealed class InputBoundsJsonConverter : JsonConverter<InputBounds>
{
    public override bool HandleNull => true;

    public override InputBounds Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            InputBounds invalidBounds = new();
            invalidBounds.MarkObjectInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return invalidBounds;
        }

        InputBounds bounds = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return bounds;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("InputBounds содержит неожиданный token.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("InputBounds завершился раньше значения свойства.");
            }

            switch (propertyName)
            {
                case "left":
                    ReadInt32(ref reader, bounds.SetLeft, bounds.MarkLeftInvalid);
                    break;
                case "top":
                    ReadInt32(ref reader, bounds.SetTop, bounds.MarkTopInvalid);
                    break;
                case "right":
                    ReadInt32(ref reader, bounds.SetRight, bounds.MarkRightInvalid);
                    break;
                case "bottom":
                    ReadInt32(ref reader, bounds.SetBottom, bounds.MarkBottomInvalid);
                    break;
                default:
                    bounds.SetAdditionalProperty(propertyName, InputJsonBindingHelpers.CloneValue(ref reader));
                    break;
            }
        }

        throw new JsonException("InputBounds завершился раньше конца объекта.");
    }

    public override void Write(Utf8JsonWriter writer, InputBounds value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteInt32(writer, "left", value.HasLeft, value.HasValidLeft, value.Left);
        WriteInt32(writer, "top", value.HasTop, value.HasValidTop, value.Top);
        WriteInt32(writer, "right", value.HasRight, value.HasValidRight, value.Right);
        WriteInt32(writer, "bottom", value.HasBottom, value.HasValidBottom, value.Bottom);

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
