namespace WinBridge.Runtime.Contracts;

public sealed record ActivateWindowResult(
    string Status,
    string? Reason,
    WindowDescriptor? Window,
    bool WasMinimized,
    bool IsForeground);
