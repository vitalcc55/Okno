using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal interface IUiaSnapshotBackend
{
    Task<UiaSnapshotBackendResult> CaptureAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken);
}
