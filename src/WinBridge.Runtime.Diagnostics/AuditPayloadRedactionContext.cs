using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

public sealed record AuditPayloadRedactionContext(
    string ToolName,
    AuditPayloadKind PayloadKind,
    ToolExecutionRedactionClass RedactionClass,
    string? EventName = null);
