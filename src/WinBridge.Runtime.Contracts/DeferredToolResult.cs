namespace WinBridge.Runtime.Contracts;

public sealed record DeferredToolResult(
    string ToolName,
    string Status,
    string Reason,
    string PlannedPhase,
    string SuggestedAlternative);
