using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

public sealed class AuditLog
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    private readonly AuditLogOptions _options;
    private readonly IAuditPayloadRedactor _payloadRedactor;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private string? _lastDisplayIdentityFingerprint;

    public AuditLog(AuditLogOptions options, TimeProvider timeProvider)
        : this(options, timeProvider, new AuditPayloadRedactor())
    {
    }

    public AuditLog(AuditLogOptions options, TimeProvider timeProvider, IAuditPayloadRedactor payloadRedactor)
    {
        _options = options;
        _payloadRedactor = payloadRedactor;
        _timeProvider = timeProvider;

        Directory.CreateDirectory(_options.RunDirectory);
        File.WriteAllText(
            _options.SummaryPath,
            $"# Okno run summary{Environment.NewLine}{Environment.NewLine}"
            + $"- run_id: `{_options.RunId}`{Environment.NewLine}"
            + $"- environment: `{_options.EnvironmentName}`{Environment.NewLine}"
            + $"- events: `{Path.GetFileName(_options.EventsPath)}`{Environment.NewLine}{Environment.NewLine}",
            FileEncoding);
    }

    public AuditInvocationScope BeginInvocation(
        string toolName,
        object? request,
        SessionSnapshot snapshot,
        ToolExecutionPolicyDescriptor? executionPolicy = null)
    {
        Activity? activity = AuditConstants.ActivitySource.StartActivity(toolName, ActivityKind.Internal);
        AuditToolContext toolContext = AuditToolContext.Resolve(toolName, executionPolicy);
        AuditRedactionResult requestRedaction = SafeRedact(
            new AuditPayloadRedactionContext(toolName, AuditPayloadKind.Request, toolContext.RedactionClass),
            request);

        Dictionary<string, string?> data = new(StringComparer.Ordinal)
        {
            ["mode"] = snapshot.Mode,
            ["attached_hwnd"] = snapshot.AttachedWindow?.Window.Hwnd.ToString(CultureInfo.InvariantCulture),
        };
        ApplyRedactionMetadata(data, requestRedaction, toolContext.RedactionClass, "request_summary", "request_summary_suppressed");

        WriteEvent(
            severity: "info",
            eventName: "tool.invocation.started",
            messageHuman: $"Запущен вызов `{toolName}`.",
            toolName: toolContext.ToolName,
            outcome: "started",
            windowHwnd: snapshot.AttachedWindow?.Window.Hwnd,
            data: data);

        return new AuditInvocationScope(this, activity, toolContext);
    }

    public void RecordSessionAttached(SessionSnapshot before, SessionSnapshot after)
    {
        long? previousHwnd = before.AttachedWindow?.Window.Hwnd;
        long? currentHwnd = after.AttachedWindow?.Window.Hwnd;

        if (previousHwnd == currentHwnd && before.Mode == after.Mode)
        {
            return;
        }

        WriteEvent(
            severity: "info",
            eventName: "session.attached",
            messageHuman: currentHwnd is null
                ? "Сессия переведена в desktop mode."
                : $"Сессия прикреплена к окну `{currentHwnd}`.",
            toolName: null,
            outcome: "state_changed",
            windowHwnd: currentHwnd,
            data: new Dictionary<string, string?>
            {
                ["previous_hwnd"] = previousHwnd?.ToString(CultureInfo.InvariantCulture),
                ["current_hwnd"] = currentHwnd?.ToString(CultureInfo.InvariantCulture),
                ["mode"] = after.Mode,
                ["match_strategy"] = after.AttachedWindow?.MatchStrategy,
            });
    }

    public void RecordDisplayIdentityStateChange(DisplayIdentityDiagnostics diagnostics, int activeMonitorCount)
    {
        string fingerprint = string.Join(
            "|",
            diagnostics.IdentityMode,
            diagnostics.FailedStage ?? string.Empty,
            diagnostics.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            diagnostics.ErrorName ?? string.Empty,
            activeMonitorCount.ToString(CultureInfo.InvariantCulture));

        bool changed;
        lock (_sync)
        {
            changed = !string.Equals(_lastDisplayIdentityFingerprint, fingerprint, StringComparison.Ordinal);
            if (changed)
            {
                _lastDisplayIdentityFingerprint = fingerprint;
            }
        }

        if (!changed)
        {
            return;
        }

        WriteEvent(
            severity: diagnostics.IdentityMode == DisplayIdentityModeValues.GdiFallback ? "warning" : "info",
            eventName: "display.identity.state_changed",
            messageHuman: diagnostics.MessageHuman,
            toolName: null,
            outcome: diagnostics.IdentityMode,
            windowHwnd: null,
            data: new Dictionary<string, string?>
            {
                ["identity_mode"] = diagnostics.IdentityMode,
                ["failed_stage"] = diagnostics.FailedStage,
                ["error_code"] = diagnostics.ErrorCode?.ToString(CultureInfo.InvariantCulture),
                ["error_name"] = diagnostics.ErrorName,
                ["active_monitor_count"] = activeMonitorCount.ToString(CultureInfo.InvariantCulture),
            });
    }

    public void RecordRuntimeEvent(
        string eventName,
        string severity,
        string messageHuman,
        string? toolName,
        string? outcome,
        long? windowHwnd,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageHuman);

        AuditToolContext? toolContext = toolName is null ? null : AuditToolContext.Resolve(toolName);
        Dictionary<string, string?>? sanitizedData = SanitizeEventData(toolContext, eventName, data);
        string sanitizedMessage = SanitizeMessageHuman(
            toolContext,
            eventName,
            messageHuman,
            outcome,
            preserveProvidedFailureMessage: false);

        WriteEvent(
            severity: severity,
            eventName: eventName,
            messageHuman: sanitizedMessage,
            toolName: toolName,
            outcome: outcome,
            windowHwnd: windowHwnd,
            data: sanitizedData);
    }

    internal void WriteToolCompleted(
        AuditToolContext toolContext,
        string outcome,
        string message,
        long? windowHwnd,
        IReadOnlyDictionary<string, string?>? data,
        Exception? exception = null)
    {
        Dictionary<string, string?> sanitizedData = SanitizeEventData(toolContext, "tool.invocation.completed", data)
            ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        ApplyDecisionMetadata(toolContext.Decision, sanitizedData);

        if (exception is not null)
        {
            sanitizedData["exception_type"] = exception.GetType().FullName;
            AuditRedactionResult exceptionRedaction = SafeRedact(
                new AuditPayloadRedactionContext(
                    toolContext.ToolName,
                    AuditPayloadKind.Exception,
                    toolContext.RedactionClass,
                    EventName: "tool.invocation.completed"),
                exception);
            ApplyRedactionMetadata(
                sanitizedData,
                exceptionRedaction,
                toolContext.RedactionClass,
                summaryFieldName: null,
                suppressedFieldName: "exception_message_suppressed");
        }

        string sanitizedMessage = SanitizeMessageHuman(
            toolContext,
            "tool.invocation.completed",
            message,
            outcome,
            preserveProvidedFailureMessage: exception is not null);

        WriteEvent(
            severity: outcome is "failed" or "ambiguous" ? "warning" : "info",
            eventName: "tool.invocation.completed",
            messageHuman: sanitizedMessage,
            toolName: toolContext.ToolName,
            outcome: outcome,
            windowHwnd: windowHwnd,
            data: sanitizedData);
    }

    private void WriteEvent(
        string severity,
        string eventName,
        string messageHuman,
        string? toolName,
        string? outcome,
        long? windowHwnd,
        IReadOnlyDictionary<string, string?>? data)
    {
        DateTimeOffset timestamp = _timeProvider.GetUtcNow();
        Activity? activity = Activity.Current;

        AuditEvent auditEvent = new(
            SchemaVersion: AuditConstants.SchemaVersion,
            TimestampUtc: timestamp.ToString("O"),
            Service: AuditConstants.ServiceName,
            Environment: _options.EnvironmentName,
            Severity: severity,
            EventName: eventName,
            MessageHuman: messageHuman,
            RunId: _options.RunId,
            TraceId: activity?.TraceId.ToString(),
            SpanId: activity?.SpanId.ToString(),
            ToolName: toolName,
            Outcome: outcome,
            WindowHwnd: windowHwnd,
            Data: data ?? new Dictionary<string, string?>());

        string serialized = JsonSerializer.Serialize(auditEvent, JsonOptions);
        string summaryLine =
            $"- {timestamp:O} [{severity}] `{eventName}`"
            + (toolName is null ? string.Empty : $" `{toolName}`")
            + $": {messageHuman}{Environment.NewLine}";

        lock (_sync)
        {
            File.AppendAllText(_options.EventsPath, serialized + Environment.NewLine, FileEncoding);
            File.AppendAllText(_options.SummaryPath, summaryLine, FileEncoding);
        }
    }

    private Dictionary<string, string?>? SanitizeEventData(
        AuditToolContext? toolContext,
        string eventName,
        IReadOnlyDictionary<string, string?>? data)
    {
        if (data is null)
        {
            return null;
        }

        if (toolContext is null)
        {
            return new Dictionary<string, string?>(data, StringComparer.Ordinal);
        }

        AuditRedactionResult eventRedaction = SafeRedact(
            new AuditPayloadRedactionContext(
                toolContext.ToolName,
                AuditPayloadKind.EventData,
                toolContext.RedactionClass,
                EventName: eventName),
            data);
        Dictionary<string, string?> mutable = new(StringComparer.Ordinal);

        if (!eventRedaction.SummarySuppressed)
        {
            foreach ((string key, string? value) in eventRedaction.SanitizedData)
            {
                mutable[key] = value;
            }
        }

        ApplyRedactionMetadata(
            mutable,
            eventRedaction,
            toolContext.RedactionClass,
            summaryFieldName: null,
            suppressedFieldName: "event_data_suppressed");
        return mutable;
    }

    private static string SanitizeMessageHuman(
        AuditToolContext? toolContext,
        string eventName,
        string messageHuman,
        string? outcome,
        bool preserveProvidedFailureMessage)
    {
        if (toolContext is null
            || toolContext.RedactionClass == ToolExecutionRedactionClass.None
            || preserveProvidedFailureMessage
            || !RequiresSafeFailureMessage(eventName, outcome))
        {
            return messageHuman;
        }

        return eventName switch
        {
            "tool.invocation.completed" => outcome == "ambiguous"
                ? "Вызов инструмента завершился неоднозначно."
                : "Вызов инструмента завершился с ошибкой.",
            "wait.runtime.completed" => "Runtime wait завершился без безопасного раскрытия детали.",
            "uia.snapshot.runtime.completed" => "Runtime UIA snapshot завершился с ошибкой.",
            _ => "Runtime событие завершилось без безопасного раскрытия детали.",
        };
    }

    private static bool RequiresSafeFailureMessage(string eventName, string? outcome) =>
        eventName switch
        {
            "tool.invocation.completed" => outcome is "failed" or "ambiguous",
            "wait.runtime.completed" => outcome is not "done",
            "uia.snapshot.runtime.completed" => outcome is not "done",
            _ => false,
        };

    private static void ApplyDecisionMetadata(ToolExecutionDecision? decision, IDictionary<string, string?> data)
    {
        if (decision is null)
        {
            return;
        }

        data["decision"] = ToSnakeCase(decision.Kind);
        data["risk_level"] = ToSnakeCase(decision.RiskLevel);
        data["guard_capability"] = decision.GuardCapability;
        data["requires_confirmation"] = ToInvariantBoolean(decision.RequiresConfirmation);
        data["dry_run_supported"] = ToInvariantBoolean(decision.DryRunSupported);
        if (decision.Reasons.Count > 0)
        {
            data["reason_codes"] = string.Join(",", decision.Reasons.Select(item => item.Code));
        }
    }

    private static void ApplyRedactionMetadata(
        IDictionary<string, string?> data,
        AuditRedactionResult redaction,
        ToolExecutionRedactionClass redactionClass,
        string? summaryFieldName,
        string? suppressedFieldName)
    {
        if (!redaction.RedactionApplied && !redaction.SummarySuppressed)
        {
            if (summaryFieldName is not null && redaction.Summary is not null)
            {
                data[summaryFieldName] = redaction.Summary;
            }

            return;
        }

        data["redaction_applied"] = ToInvariantBoolean(redaction.RedactionApplied || redaction.SummarySuppressed);
        data["redaction_class"] = ToSnakeCase(redactionClass);
        if (redaction.RedactedFields.Count > 0)
        {
            data["redacted_fields"] = string.Join(",", redaction.RedactedFields);
        }

        if (summaryFieldName is not null && redaction.Summary is not null)
        {
            data[summaryFieldName] = redaction.Summary;
        }

        if (redaction.SummarySuppressed && suppressedFieldName is not null)
        {
            data[suppressedFieldName] = ToInvariantBoolean(true);
        }
    }

    private static string ToInvariantBoolean(bool value) =>
        value ? "true" : "false";

    private static string ToSnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    private AuditRedactionResult SafeRedact(AuditPayloadRedactionContext context, object? payload)
    {
        try
        {
            return _payloadRedactor.Redact(context, payload);
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
}
