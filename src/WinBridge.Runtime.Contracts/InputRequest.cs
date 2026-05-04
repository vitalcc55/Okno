// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

[JsonConverter(typeof(InputRequestJsonConverter))]
public sealed record InputRequest
{
    private bool _hasValidObject = true;
    private long? _hwnd;
    private bool _hasHwnd;
    private bool _hasValidHwnd = true;
    private IReadOnlyList<InputAction> _actions = [];
    private bool _hasActions;
    private bool _hasValidActions = true;
    private bool _confirm;
    private bool _hasConfirm;
    private bool _hasValidConfirm = true;
    private IDictionary<string, JsonElement>? _additionalProperties;

    [JsonPropertyName("hwnd")]
    public long? Hwnd
    {
        get => _hwnd;
        init => SetHwnd(value);
    }

    [JsonPropertyName("actions")]
    public IReadOnlyList<InputAction> Actions
    {
        get => _actions;
        init => SetActions(value);
    }

    [JsonPropertyName("confirm")]
    public bool Confirm
    {
        get => _confirm;
        init => SetConfirm(value);
    }

    [JsonIgnore]
    public bool HasValidObject => _hasValidObject;

    [JsonIgnore]
    public bool HasHwnd => _hasHwnd;

    [JsonIgnore]
    public bool HasValidHwnd => _hasValidHwnd;

    [JsonIgnore]
    public bool HasActions => _hasActions;

    [JsonIgnore]
    public bool HasValidActions => _hasValidActions;

    [JsonIgnore]
    public bool HasConfirm => _hasConfirm;

    [JsonIgnore]
    public bool HasValidConfirm => _hasValidConfirm;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value;
    }

    internal void SetHwnd(long? value)
    {
        _hwnd = value;
        _hasHwnd = true;
        _hasValidHwnd = true;
    }

    internal void MarkHwndInvalid()
    {
        _hwnd = null;
        _hasHwnd = true;
        _hasValidHwnd = false;
    }

    internal void SetActions(IReadOnlyList<InputAction>? value)
    {
        _actions = value ?? [];
        _hasActions = true;
        _hasValidActions = true;
    }

    internal void MarkActionsInvalid()
    {
        _actions = [];
        _hasActions = true;
        _hasValidActions = false;
    }

    internal void SetConfirm(bool value)
    {
        _confirm = value;
        _hasConfirm = true;
        _hasValidConfirm = true;
    }

    internal void MarkConfirmInvalid()
    {
        _confirm = false;
        _hasConfirm = true;
        _hasValidConfirm = false;
    }

    internal void MarkObjectInvalid()
    {
        _hasValidObject = false;
    }

    internal void SetAdditionalProperty(string name, JsonElement value)
    {
        (_additionalProperties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[name] = value;
    }
}

internal sealed class InputRequestJsonConverter : JsonConverter<InputRequest>
{
    public override bool HandleNull => true;

    public override InputRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            InputRequest invalidRequest = new();
            invalidRequest.MarkObjectInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return invalidRequest;
        }

        InputRequest request = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return request;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("InputRequest содержит неожиданный token.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("InputRequest завершился раньше значения свойства.");
            }

            switch (propertyName)
            {
                case "hwnd":
                    ReadNullableInt64(ref reader, request.SetHwnd, request.MarkHwndInvalid);
                    break;
                case "actions":
                    ReadActions(ref reader, options, request);
                    break;
                case "confirm":
                    ReadBoolean(ref reader, request.SetConfirm, request.MarkConfirmInvalid);
                    break;
                default:
                    request.SetAdditionalProperty(propertyName, InputJsonBindingHelpers.CloneValue(ref reader));
                    break;
            }
        }

        throw new JsonException("InputRequest завершился раньше конца объекта.");
    }

    public override void Write(Utf8JsonWriter writer, InputRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.HasHwnd)
        {
            if (value.HasValidHwnd && value.Hwnd is not null)
            {
                writer.WriteNumber("hwnd", value.Hwnd.Value);
            }
            else
            {
                writer.WriteNull("hwnd");
            }
        }

        if (value.HasActions)
        {
            writer.WritePropertyName("actions");
            if (value.HasValidActions)
            {
                JsonSerializer.Serialize(writer, value.Actions, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }

        if (value.HasConfirm)
        {
            if (value.HasValidConfirm)
            {
                writer.WriteBoolean("confirm", value.Confirm);
            }
            else
            {
                writer.WriteNull("confirm");
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

    private static void ReadActions(ref Utf8JsonReader reader, JsonSerializerOptions options, InputRequest request)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            request.SetActions(null);
            return;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            request.MarkActionsInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return;
        }

        List<InputAction> actions = [];
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                request.SetActions(actions);
                return;
            }

            InputAction? action = JsonSerializer.Deserialize<InputAction>(ref reader, options);
            actions.Add(action ?? InputAction.CreateInvalidObject());
        }

        throw new JsonException("InputRequest.actions завершился раньше конца массива.");
    }

    private static void ReadNullableInt64(ref Utf8JsonReader reader, Action<long?> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            assign(null);
            return;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long value))
        {
            assign(value);
            return;
        }

        markInvalid();
        InputJsonBindingHelpers.SkipNestedValue(ref reader);
    }

    private static void ReadBoolean(ref Utf8JsonReader reader, Action<bool> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            assign(true);
            return;
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            assign(false);
            return;
        }

        markInvalid();
        InputJsonBindingHelpers.SkipNestedValue(ref reader);
    }
}
