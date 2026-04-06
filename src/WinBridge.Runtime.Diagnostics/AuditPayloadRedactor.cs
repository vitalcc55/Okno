using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

public sealed class AuditPayloadRedactor : IAuditPayloadRedactor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly HashSet<string> SafeTargetMetadataStringProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "automationId",
        "className",
        "condition",
        "controlType",
        "guardCapability",
        "matchStrategy",
        "mode",
        "monitorId",
        "processName",
        "riskLevel",
        "scope",
        "source",
        "status",
        "targetSource",
    };

    public AuditRedactionResult Redact(AuditPayloadRedactionContext context, object? payload)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (payload is null)
        {
            return AuditRedactionResult.None();
        }

        try
        {
            if (context.PayloadKind == AuditPayloadKind.Exception && payload is Exception)
            {
                return new AuditRedactionResult(
                    Summary: null,
                    SanitizedData: new Dictionary<string, string?>(StringComparer.Ordinal),
                    RedactedFields: ["exception_message"],
                    RedactionApplied: true,
                    SummarySuppressed: true);
            }

            if (payload is IReadOnlyDictionary<string, string?> readOnlyData)
            {
                return RedactEventData(context, readOnlyData);
            }

            if (payload is IDictionary<string, string?> mutableData)
            {
                return RedactEventData(
                    context,
                    new Dictionary<string, string?>(mutableData, StringComparer.Ordinal));
            }

            JsonElement payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
            List<string> redactedFields = [];
            object? sanitized = SanitizeElement(
                payloadElement,
                context.RedactionClass,
                propertyName: null,
                propertyPath: null,
                redactedFields);

            string summary = Summarize(sanitized);
            return new AuditRedactionResult(
                Summary: summary,
                SanitizedData: new Dictionary<string, string?>(StringComparer.Ordinal),
                RedactedFields: redactedFields,
                RedactionApplied: redactedFields.Count > 0,
                SummarySuppressed: false);
        }
        catch (Exception)
        {
            return new AuditRedactionResult(
                Summary: null,
                SanitizedData: new Dictionary<string, string?>(StringComparer.Ordinal),
                RedactedFields: Array.Empty<string>(),
                RedactionApplied: false,
                SummarySuppressed: true);
        }
    }

    private static AuditRedactionResult RedactEventData(
        AuditPayloadRedactionContext context,
        IReadOnlyDictionary<string, string?> data)
    {
        Dictionary<string, string?> sanitizedData = new(StringComparer.Ordinal);
        List<string> redactedFields = [];

        foreach ((string key, string? value) in data)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                sanitizedData[key] = value;
                continue;
            }

            if (TrySanitizeEventField(context.RedactionClass, key, value, out string? sanitizedValue, out bool fieldRedacted))
            {
                if (sanitizedValue is not null)
                {
                    sanitizedData[key] = sanitizedValue;
                }

                if (fieldRedacted)
                {
                    redactedFields.Add(key);
                }

                continue;
            }

            if (string.Equals(key, "exception_message", StringComparison.Ordinal))
            {
                redactedFields.Add(key);
                continue;
            }

            if (ShouldRedactEventField(context.RedactionClass, key))
            {
                redactedFields.Add(key);
                continue;
            }

            sanitizedData[key] = value;
        }

        return new AuditRedactionResult(
            Summary: null,
            SanitizedData: sanitizedData,
            RedactedFields: redactedFields,
            RedactionApplied: redactedFields.Count > 0,
            SummarySuppressed: false);
    }

    private static bool TrySanitizeEventField(
        ToolExecutionRedactionClass redactionClass,
        string key,
        string value,
        out string? sanitizedValue,
        out bool fieldRedacted)
    {
        sanitizedValue = null;
        fieldRedacted = false;

        if (redactionClass == ToolExecutionRedactionClass.LaunchPayload
            && (string.Equals(key, "executable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "executable_identity", StringComparison.OrdinalIgnoreCase)))
        {
            string? executableIdentity = ResolveLaunchExecutableIdentity(value);
            if (string.IsNullOrWhiteSpace(executableIdentity))
            {
                fieldRedacted = true;
                return true;
            }

            sanitizedValue = executableIdentity;
            fieldRedacted = !string.Equals(executableIdentity, value, StringComparison.Ordinal);
            return true;
        }

        return false;
    }

    private static bool ShouldRedactEventField(ToolExecutionRedactionClass redactionClass, string key) =>
        redactionClass switch
        {
            ToolExecutionRedactionClass.TextPayload => MatchesAny(key, "expected_text", "text", "value", "name"),
            ToolExecutionRedactionClass.ClipboardPayload => MatchesAny(key, "clipboard", "content", "text", "value"),
            ToolExecutionRedactionClass.LaunchPayload => MatchesAny(
                key,
                "command",
                "command_line",
                "args",
                "arguments",
                "environment",
                "env",
                "path",
                "target",
                "uri",
                "url",
                "working_directory",
                "workingDirectory"),
            ToolExecutionRedactionClass.TargetMetadata => MatchesAny(key, "text", "value", "title", "title_pattern"),
            _ => false,
        };

    private static object? SanitizeElement(
        JsonElement element,
        ToolExecutionRedactionClass redactionClass,
        string? propertyName,
        string? propertyPath,
        List<string> redactedFields)
    {
        if (propertyName is not null && ShouldRedactWholeProperty(redactionClass, propertyName))
        {
            return CreateRedactedMarker(element, propertyPath ?? propertyName, redactedFields);
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeObject(element, redactionClass, propertyPath, redactedFields),
            JsonValueKind.Array => SanitizeArray(element, redactionClass, propertyName, propertyPath, redactedFields),
            JsonValueKind.String => SanitizeStringValue(element.GetString(), redactionClass, propertyName, propertyPath, redactedFields),
            JsonValueKind.Number => element.TryGetInt64(out long integral)
                ? integral
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
    }

    private static Dictionary<string, object?> SanitizeObject(
        JsonElement element,
        ToolExecutionRedactionClass redactionClass,
        string? propertyPath,
        List<string> redactedFields)
    {
        Dictionary<string, object?> sanitized = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string childPath = propertyPath is null ? property.Name : propertyPath + "." + property.Name;
            sanitized[property.Name] = SanitizeElement(
                property.Value,
                redactionClass,
                property.Name,
                childPath,
                redactedFields);
        }

        return sanitized;
    }

    private static List<object?> SanitizeArray(
        JsonElement element,
        ToolExecutionRedactionClass redactionClass,
        string? propertyName,
        string? propertyPath,
        List<string> redactedFields)
    {
        List<object?> sanitized = [];
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string childPath = propertyPath is null ? $"[{index}]" : $"{propertyPath}[{index}]";
            sanitized.Add(SanitizeElement(item, redactionClass, propertyName, childPath, redactedFields));
            index++;
        }

        return sanitized;
    }

    private static object? SanitizeStringValue(
        string? value,
        ToolExecutionRedactionClass redactionClass,
        string? propertyName,
        string? propertyPath,
        List<string> redactedFields)
    {
        if (value is null)
        {
            return null;
        }

        if (redactionClass == ToolExecutionRedactionClass.None)
        {
            return value;
        }

        if (redactionClass == ToolExecutionRedactionClass.LaunchPayload
            && propertyName is not null
            && string.Equals(propertyName, "executable", StringComparison.OrdinalIgnoreCase))
        {
            string? executableIdentity = ResolveLaunchExecutableIdentity(value);
            return executableIdentity is not null
                ? executableIdentity
                : CreateRedactedStringMarker(value, propertyPath ?? propertyName, redactedFields);
        }

        if (!ShouldRedactStringValue(redactionClass, propertyName))
        {
            return value;
        }

        return CreateRedactedStringMarker(value, propertyPath ?? propertyName ?? "value", redactedFields);
    }

    private static bool ShouldRedactWholeProperty(ToolExecutionRedactionClass redactionClass, string propertyName) =>
        redactionClass switch
        {
            ToolExecutionRedactionClass.ClipboardPayload => MatchesAny(propertyName, "clipboard", "content", "data", "value"),
            ToolExecutionRedactionClass.LaunchPayload => MatchesAny(propertyName, "args", "arguments", "environment", "env", "workingDirectory", "path", "target", "uri", "url", "command", "commandLine"),
            ToolExecutionRedactionClass.ArtifactReference => MatchesAny(propertyName, "content", "contents", "data"),
            _ => false,
        };

    private static bool ShouldRedactStringValue(ToolExecutionRedactionClass redactionClass, string? propertyName)
    {
        if (propertyName is null)
        {
            return redactionClass is not ToolExecutionRedactionClass.None;
        }

        return redactionClass switch
        {
            ToolExecutionRedactionClass.TargetMetadata =>
                !SafeTargetMetadataStringProperties.Contains(propertyName)
                || MatchesAny(propertyName, "title", "titlePattern", "name", "text", "value"),
            ToolExecutionRedactionClass.TextPayload =>
                MatchesAny(propertyName, "expectedText", "text", "value", "name", "titlePattern"),
            ToolExecutionRedactionClass.ClipboardPayload =>
                !MatchesAny(propertyName, "contentKind", "format", "source"),
            ToolExecutionRedactionClass.LaunchPayload =>
                !string.Equals(propertyName, "executable", StringComparison.OrdinalIgnoreCase),
            ToolExecutionRedactionClass.ArtifactReference =>
                !MatchesAny(propertyName, "artifactPath", "diagnosticArtifactPath", "mimeType", "byteSize", "status"),
            _ => false,
        };
    }

    private static string? ResolveLaunchExecutableIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string normalized = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        string executableName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        if (normalized.Contains(Path.DirectorySeparatorChar) && !Path.HasExtension(executableName))
        {
            return null;
        }

        return executableName;
    }

    private static Dictionary<string, object?> CreateRedactedMarker(
        JsonElement element,
        string fieldPath,
        List<string> redactedFields)
    {
        RegisterRedactedField(redactedFields, fieldPath);

        return element.ValueKind switch
        {
            JsonValueKind.String => CreateRedactedStringMarker(element.GetString(), fieldPath, redactedFields),
            JsonValueKind.Array => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["redacted"] = true,
                ["count"] = element.GetArrayLength(),
            },
            JsonValueKind.Object => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["redacted"] = true,
                ["fieldCount"] = element.EnumerateObject().Count(),
            },
            _ => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["redacted"] = true,
            },
        };
    }

    private static Dictionary<string, object?> CreateRedactedStringMarker(
        string? value,
        string fieldPath,
        List<string> redactedFields)
    {
        RegisterRedactedField(redactedFields, fieldPath);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["redacted"] = true,
            ["length"] = value?.Length ?? 0,
        };
    }

    private static void RegisterRedactedField(List<string> redactedFields, string fieldPath)
    {
        if (!redactedFields.Contains(fieldPath, StringComparer.Ordinal))
        {
            redactedFields.Add(fieldPath);
        }
    }

    private static bool MatchesAny(string value, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private static string Summarize(object? value)
    {
        string raw = JsonSerializer.Serialize(value, JsonOptions);
        return raw.Length <= 240 ? raw : raw[..240] + "...";
    }
}
