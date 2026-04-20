using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBridge.Runtime.Contracts;

[JsonConverter(typeof(InputTargetIdentityJsonConverter))]
public sealed record InputTargetIdentity
{
    private bool _hasValidObject = true;
    private long _hwnd;
    private bool _hasHwnd;
    private bool _hasValidHwnd;
    private int _processId;
    private bool _hasProcessId;
    private bool _hasValidProcessId;
    private int _threadId;
    private bool _hasThreadId;
    private bool _hasValidThreadId;
    private string? _className;
    private bool _hasClassName;
    private bool _hasValidClassName;
    private IDictionary<string, JsonElement>? _additionalProperties;

    public InputTargetIdentity()
    {
    }

    public InputTargetIdentity(long hwnd, int processId, int threadId, string className)
    {
        Hwnd = hwnd;
        ProcessId = processId;
        ThreadId = threadId;
        ClassName = className;
    }

    [JsonPropertyName("hwnd")]
    public long Hwnd
    {
        get => _hwnd;
        init => SetHwnd(value);
    }

    [JsonPropertyName("processId")]
    public int ProcessId
    {
        get => _processId;
        init => SetProcessId(value);
    }

    [JsonPropertyName("threadId")]
    public int ThreadId
    {
        get => _threadId;
        init => SetThreadId(value);
    }

    [JsonPropertyName("className")]
    public string? ClassName
    {
        get => _className;
        init => SetClassName(value);
    }

    [JsonIgnore]
    public bool HasValidObject => _hasValidObject;

    [JsonIgnore]
    public bool HasHwnd => _hasHwnd;

    [JsonIgnore]
    public bool HasValidHwnd => _hasValidHwnd;

    [JsonIgnore]
    public bool HasProcessId => _hasProcessId;

    [JsonIgnore]
    public bool HasValidProcessId => _hasValidProcessId;

    [JsonIgnore]
    public bool HasThreadId => _hasThreadId;

    [JsonIgnore]
    public bool HasValidThreadId => _hasValidThreadId;

    [JsonIgnore]
    public bool HasClassName => _hasClassName;

    [JsonIgnore]
    public bool HasValidClassName => _hasValidClassName;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value;
    }

    internal void SetHwnd(long value)
    {
        _hwnd = value;
        _hasHwnd = true;
        _hasValidHwnd = true;
    }

    internal void SetProcessId(int value)
    {
        _processId = value;
        _hasProcessId = true;
        _hasValidProcessId = true;
    }

    internal void SetThreadId(int value)
    {
        _threadId = value;
        _hasThreadId = true;
        _hasValidThreadId = true;
    }

    internal void SetClassName(string? value)
    {
        _className = value;
        _hasClassName = true;
        _hasValidClassName = value is not null;
    }

    internal void MarkHwndInvalid()
    {
        _hwnd = default;
        _hasHwnd = true;
        _hasValidHwnd = false;
    }

    internal void MarkProcessIdInvalid()
    {
        _processId = default;
        _hasProcessId = true;
        _hasValidProcessId = false;
    }

    internal void MarkThreadIdInvalid()
    {
        _threadId = default;
        _hasThreadId = true;
        _hasValidThreadId = false;
    }

    internal void MarkClassNameInvalid()
    {
        _className = null;
        _hasClassName = true;
        _hasValidClassName = false;
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

internal sealed class InputTargetIdentityJsonConverter : JsonConverter<InputTargetIdentity>
{
    public override bool HandleNull => true;

    public override InputTargetIdentity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            InputTargetIdentity invalidIdentity = new();
            invalidIdentity.MarkObjectInvalid();
            InputJsonBindingHelpers.SkipNestedValue(ref reader);
            return invalidIdentity;
        }

        InputTargetIdentity identity = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return identity;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("InputTargetIdentity содержит неожиданный token.");
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("InputTargetIdentity завершился раньше значения свойства.");
            }

            switch (propertyName)
            {
                case "hwnd":
                    ReadInt64(ref reader, identity.SetHwnd, identity.MarkHwndInvalid);
                    break;
                case "processId":
                    ReadInt32(ref reader, identity.SetProcessId, identity.MarkProcessIdInvalid);
                    break;
                case "threadId":
                    ReadInt32(ref reader, identity.SetThreadId, identity.MarkThreadIdInvalid);
                    break;
                case "className":
                    ReadString(ref reader, identity.SetClassName, identity.MarkClassNameInvalid);
                    break;
                default:
                    identity.SetAdditionalProperty(propertyName, InputJsonBindingHelpers.CloneValue(ref reader));
                    break;
            }
        }

        throw new JsonException("InputTargetIdentity завершился раньше конца объекта.");
    }

    public override void Write(Utf8JsonWriter writer, InputTargetIdentity value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteInt64(writer, "hwnd", value.HasHwnd, value.HasValidHwnd, value.Hwnd);
        WriteInt32(writer, "processId", value.HasProcessId, value.HasValidProcessId, value.ProcessId);
        WriteInt32(writer, "threadId", value.HasThreadId, value.HasValidThreadId, value.ThreadId);
        WriteString(writer, "className", value.HasClassName, value.HasValidClassName, value.ClassName);

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

    private static void ReadInt64(ref Utf8JsonReader reader, Action<long> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long value))
        {
            assign(value);
            return;
        }

        markInvalid();
        InputJsonBindingHelpers.SkipNestedValue(ref reader);
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

    private static void ReadString(ref Utf8JsonReader reader, Action<string?> assign, Action markInvalid)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            assign(reader.GetString());
            return;
        }

        markInvalid();
        InputJsonBindingHelpers.SkipNestedValue(ref reader);
    }

    private static void WriteInt64(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, long value)
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

    private static void WriteString(Utf8JsonWriter writer, string propertyName, bool isPresent, bool isValid, string? value)
    {
        if (!isPresent)
        {
            return;
        }

        if (isValid && value is not null)
        {
            writer.WriteString(propertyName, value);
            return;
        }

        writer.WriteNull(propertyName);
    }
}
