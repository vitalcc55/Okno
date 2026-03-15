namespace WinBridge.Runtime.Contracts;

public sealed record CaptureTarget(
    CaptureScope Scope,
    WindowDescriptor? Window,
    string? MonitorId = null);
