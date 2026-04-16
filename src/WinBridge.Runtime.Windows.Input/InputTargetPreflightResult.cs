namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputTargetPreflightResult(
    bool IsAllowed,
    string? FailureCode = null,
    string? Reason = null);
