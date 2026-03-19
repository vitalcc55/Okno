using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiaSnapshotWorkerInvocation(
    WindowDescriptor TargetWindow,
    UiaSnapshotRequest Request);
