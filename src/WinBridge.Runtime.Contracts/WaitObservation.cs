namespace WinBridge.Runtime.Contracts;

public sealed record WaitObservation(
    int? MatchCount = null,
    bool? TargetIsForeground = null,
    string? MatchedText = null,
    string? MatchedTextSource = null,
    string? DiagnosticArtifactPath = null,
    string? Detail = null);
