using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiaSnapshotBackendResult(
    bool Success,
    string? Reason,
    string? FailureStage,
    DateTimeOffset CapturedAtUtc,
    UiaElementSnapshot? Root = null,
    int RealizedDepth = 0,
    int NodeCount = 0,
    bool Truncated = false,
    bool DepthBoundaryReached = false,
    bool NodeBudgetBoundaryReached = false,
    string? DiagnosticArtifactPath = null);
