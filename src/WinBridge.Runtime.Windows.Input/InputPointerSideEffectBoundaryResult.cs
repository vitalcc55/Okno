namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputPointerSideEffectBoundaryResult(
    bool Success,
    bool MouseButtonsSwapped = false,
    string? FailureCode = null,
    string? Reason = null);
