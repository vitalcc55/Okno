namespace WinBridge.Runtime.Contracts;

public sealed record FocusWindowResult(
    string Status,
    string? Reason,
    WindowDescriptor? Window);
