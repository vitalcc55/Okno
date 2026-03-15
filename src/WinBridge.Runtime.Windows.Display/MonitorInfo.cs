using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

public sealed record MonitorInfo(MonitorDescriptor Descriptor, long Handle);
