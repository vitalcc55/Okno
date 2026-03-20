using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiAutomationWorkerOperationValues
{
    public const string Snapshot = "snapshot";
    public const string WaitProbe = "wait_probe";
}

internal sealed record UiAutomationWorkerInvocation(
    string Operation,
    WindowDescriptor TargetWindow,
    UiaSnapshotRequest? SnapshotRequest = null,
    WaitRequest? WaitProbeRequest = null);
