using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinAppDiscoveryService(
    IWindowManager windowManager,
    ComputerUseWinApprovalStore approvalStore)
{
    public IReadOnlyList<ComputerUseWinAppDescriptor> ListVisibleApps() =>
        windowManager.ListWindows()
            .Where(static item => item.IsVisible)
            .Select(static item => ComputerUseWinAppIdentity.TryCreateStableAppId(item, out string? appId)
                ? new StableIdentityWindow(item, appId!)
                : null)
            .Where(static item => item is not null)
            .Cast<StableIdentityWindow>()
            .GroupBy(static item => item.AppId, static item => item.Window)
            .Select(group =>
            {
                WindowDescriptor selected = group
                    .OrderByDescending(static item => item.IsForeground)
                    .ThenByDescending(static item => item.IsVisible)
                    .ThenBy(static item => item.Hwnd)
                    .First();

                bool isBlocked = ComputerUseWinTargetPolicy.TryGetBlockedReason(selected, out string? blockReason);
                return new ComputerUseWinAppDescriptor(
                    AppId: group.Key,
                    Hwnd: selected.Hwnd,
                    Title: selected.Title,
                    ProcessName: selected.ProcessName,
                    ProcessId: selected.ProcessId,
                    IsForeground: selected.IsForeground,
                    IsVisible: selected.IsVisible,
                    IsApproved: approvalStore.IsApproved(group.Key),
                    IsBlocked: isBlocked,
                    BlockReason: blockReason);
            })
            .OrderByDescending(static item => item.IsForeground)
            .ThenBy(static item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record StableIdentityWindow(WindowDescriptor Window, string AppId);
}
