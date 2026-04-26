using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinAppDiscoveryService(
    IWindowManager windowManager,
    ComputerUseWinApprovalStore approvalStore,
    ComputerUseWinExecutionTargetCatalog executionTargetCatalog)
{
    public IReadOnlyList<ComputerUseWinDiscoveredApp> ListVisibleApps() =>
        executionTargetCatalog.Materialize(windowManager.ListWindows())
            .GroupBy(static item => item.ApprovalKey.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                ComputerUseWinExecutionTarget[] windows = group
                    .OrderByDescending(static item => item.Window.IsForeground)
                    .ThenBy(static item => item.Window.Hwnd)
                    .ToArray();
                WindowDescriptor representative = windows[0].Window;

                bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(representative, out string? blockReason);
                return new ComputerUseWinDiscoveredApp(
                    ApprovalKey: windows[0].ApprovalKey,
                    Windows: windows,
                    IsApproved: approvalStore.IsApproved(group.Key),
                    IsBlocked: isBlocked,
                    BlockReason: blockReason);
            })
            .OrderByDescending(static item => item.Windows.Any(static window => window.Window.IsForeground))
            .ThenBy(static item => item.ApprovalKey.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
