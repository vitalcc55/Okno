namespace WinBridge.Runtime.Contracts;

public sealed record WindowDescriptor(
    long Hwnd,
    string Title,
    string? ProcessName,
    Bounds Bounds,
    bool IsForeground,
    bool IsVisible);
