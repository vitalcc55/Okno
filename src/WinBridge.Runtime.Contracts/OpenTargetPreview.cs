namespace WinBridge.Runtime.Contracts;

public sealed record OpenTargetPreview(
    string TargetKind,
    string? TargetIdentity = null,
    string? UriScheme = null);
