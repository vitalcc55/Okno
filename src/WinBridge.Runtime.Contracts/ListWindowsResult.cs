namespace WinBridge.Runtime.Contracts;

public sealed record ListWindowsResult(
    IReadOnlyList<WindowDescriptor> Windows,
    int Count,
    SessionSnapshot Session);
