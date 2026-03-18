namespace WinBridge.Runtime.Contracts;

public sealed record UiaSnapshotResult(
    string Status,
    string? Reason = null,
    WindowDescriptor? Window = null,
    string View = UiaSnapshotDefaults.View,
    int RequestedDepth = UiaSnapshotDefaults.Depth,
    int RealizedDepth = 0,
    int NodeCount = 0,
    bool Truncated = false,
    string? AcquisitionMode = null,
    string? ArtifactPath = null,
    DateTimeOffset CapturedAtUtc = default,
    UiaElementSnapshot? Root = null,
    SessionSnapshot? Session = null);
