using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public interface IUiAutomationService
{
    Task<UiaSnapshotResult> SnapshotAsync(
        WindowDescriptor targetWindow,
        UiaSnapshotRequest request,
        CancellationToken cancellationToken);
}
