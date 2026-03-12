namespace WinBridge.Runtime.Diagnostics;

public sealed record AuditEvent(
    string SchemaVersion,
    string TimestampUtc,
    string Service,
    string Environment,
    string Severity,
    string EventName,
    string MessageHuman,
    string RunId,
    string? TraceId,
    string? SpanId,
    string? ToolName,
    string? Outcome,
    long? WindowHwnd,
    IReadOnlyDictionary<string, string?> Data);
