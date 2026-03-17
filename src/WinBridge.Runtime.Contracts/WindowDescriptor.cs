namespace WinBridge.Runtime.Contracts;

public sealed record WindowDescriptor(
    long Hwnd,
    string Title,
    string? ProcessName,
    int? ProcessId,
    int? ThreadId,
    string? ClassName,
    Bounds Bounds,
    bool IsForeground,
    bool IsVisible,
    int EffectiveDpi = 96,
    double DpiScale = 1.0,
    string WindowState = WindowStateValues.Unknown,
    string? MonitorId = null,
    string? MonitorFriendlyName = null);
