namespace WinBridge.Runtime.Tooling;

public sealed record ToolDescriptor(
    string Name,
    string Capability,
    ToolLifecycle Lifecycle,
    ToolSafetyClass SafetyClass,
    string Summary,
    string? PlannedPhase,
    string? SuggestedAlternative,
    bool SmokeRequired,
    ToolExecutionPolicyDescriptor? ExecutionPolicy = null);
