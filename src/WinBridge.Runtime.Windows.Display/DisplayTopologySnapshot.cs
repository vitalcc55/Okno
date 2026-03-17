using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

public sealed record DisplayTopologySnapshot(
    IReadOnlyList<MonitorInfo> Monitors,
    DisplayIdentityDiagnostics Diagnostics);
