namespace WinBridge.Runtime.Windows.UIA;

public sealed record UiAutomationWaitProbeExecutionResult(
    UiAutomationWaitProbeResult Result,
    DateTimeOffset CompletedAtUtc,
    bool TimedOut = false,
    string? DiagnosticArtifactPath = null,
    DateTimeOffset? WorkerCompletedAtUtc = null);
