namespace WinBridge.Runtime.Tooling;

public enum ToolExecutionPolicyGroup
{
    Observe,
    SessionMutation,
    Launch,
    Input,
    Clipboard,
    UiaAction,
}

public enum ToolExecutionRiskLevel
{
    Low,
    Medium,
    High,
    Destructive,
}

public enum ToolExecutionConfirmationMode
{
    None,
    Required,
    Conditional,
}

public enum ToolExecutionRedactionClass
{
    None,
    TargetMetadata,
    TextPayload,
    ClipboardPayload,
    LaunchPayload,
    ArtifactReference,
}

public sealed record ToolExecutionPolicyDescriptor(
    ToolExecutionPolicyGroup PolicyGroup,
    ToolExecutionRiskLevel RiskLevel,
    string GuardCapability,
    bool SupportsDryRun,
    ToolExecutionConfirmationMode ConfirmationMode,
    ToolExecutionRedactionClass RedactionClass);
