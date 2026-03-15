namespace WinBridge.Runtime.Contracts;

public sealed record MonitorDescriptor(
    string MonitorId,
    string FriendlyName,
    string GdiDeviceName,
    Bounds Bounds,
    Bounds WorkArea,
    double DpiScale,
    bool IsPrimary);
