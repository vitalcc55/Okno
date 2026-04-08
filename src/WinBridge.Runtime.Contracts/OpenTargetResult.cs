namespace WinBridge.Runtime.Contracts;

public sealed record OpenTargetResult(
    string Status,
    string Decision,
    string? ResultMode = null,
    string? FailureCode = null,
    string? Reason = null,
    string? TargetKind = null,
    string? TargetIdentity = null,
    string? UriScheme = null,
    DateTimeOffset? AcceptedAtUtc = null,
    int? HandlerProcessId = null,
    string? ArtifactPath = null,
    OpenTargetPreview? Preview = null,
    string? RiskLevel = null,
    string? GuardCapability = null,
    bool RequiresConfirmation = false,
    bool DryRunSupported = false,
    IReadOnlyList<GuardReason>? Reasons = null);
