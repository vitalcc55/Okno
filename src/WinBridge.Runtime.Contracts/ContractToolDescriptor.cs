namespace WinBridge.Runtime.Contracts;

public sealed record ContractToolDescriptor(
    string Name,
    string Capability,
    string Lifecycle,
    string SafetyClass,
    string Summary,
    string? PlannedPhase,
    string? SuggestedAlternative,
    bool SmokeRequired);
