namespace WinBridge.Runtime.Contracts;

public sealed record ListMonitorsResult(
    IReadOnlyList<MonitorDescriptor> Monitors,
    int Count,
    SessionSnapshot Session);
