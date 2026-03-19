namespace WinBridge.Runtime.Contracts;

public sealed record UiaSnapshotToolResult(
    string Status,
    string? Reason = null,
    ObservedWindowDescriptor? Window = null,
    long? RequestedHwnd = null,
    int RequestedDepth = UiaSnapshotDefaults.Depth,
    int RequestedMaxNodes = UiaSnapshotDefaults.MaxNodes,
    string? TargetSource = null,
    string? TargetFailureCode = null,
    string View = UiaSnapshotDefaults.View,
    int RealizedDepth = 0,
    int NodeCount = 0,
    bool Truncated = false,
    bool DepthBoundaryReached = false,
    bool NodeBudgetBoundaryReached = false,
    string? AcquisitionMode = null,
    string? ArtifactPath = null,
    DateTimeOffset? CapturedAtUtc = null,
    UiaElementSnapshot? Root = null);
