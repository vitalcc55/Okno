using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;

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
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();

    public AuditLog(AuditLogOptions options, TimeProvider timeProvider)
    {
        _options = options;
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

    public AuditInvocationScope BeginInvocation(string toolName, object? request, SessionSnapshot snapshot)
    {
        Activity? activity = AuditConstants.ActivitySource.StartActivity(toolName, ActivityKind.Internal);

        WriteEvent(
            severity: "info",
            eventName: "tool.invocation.started",
            messageHuman: $"Запущен вызов `{toolName}`.",
            toolName: toolName,
            outcome: "started",
            windowHwnd: snapshot.AttachedWindow?.Window.Hwnd,
            data: new Dictionary<string, string?>
            {
                ["mode"] = snapshot.Mode,
                ["attached_hwnd"] = snapshot.AttachedWindow?.Window.Hwnd.ToString(CultureInfo.InvariantCulture),
                ["request_summary"] = Summarize(request),
            });

        return new AuditInvocationScope(this, activity, toolName);
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

    internal void WriteToolCompleted(
        string toolName,
        string outcome,
        string message,
        long? windowHwnd,
        IReadOnlyDictionary<string, string?>? data)
    {
        WriteEvent(
            severity: outcome is "failed" or "ambiguous" ? "warning" : "info",
            eventName: "tool.invocation.completed",
            messageHuman: message,
            toolName: toolName,
            outcome: outcome,
            windowHwnd: windowHwnd,
            data: data);
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

    private static string? Summarize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string raw = JsonSerializer.Serialize(value, JsonOptions);
        return raw.Length <= 240 ? raw : raw[..240] + "...";
    }
}
