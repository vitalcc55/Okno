using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class WindowTargetResolver(IWindowManager windowManager) : IWindowTargetResolver
{
    public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        return ResolveExplicitOrAttachedWindowCore(explicitHwnd, attachedWindow, liveWindows);
    }

    public WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow)
    {
        if (!WindowIdentityValidator.TryValidateStableIdentity(expectedWindow, out _))
        {
            return null;
        }

        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);
        WindowDescriptor? liveCandidate = liveWindows.FirstOrDefault(candidate => candidate.Hwnd == expectedWindow.Hwnd);
        if (liveCandidate is null)
        {
            return null;
        }

        return WindowIdentityValidator.MatchesStableIdentity(liveCandidate, expectedWindow) ? liveCandidate : null;
    }

    public UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);

        if (explicitHwnd is long hwnd)
        {
            if (hwnd <= 0)
            {
                return new(FailureCode: UiaSnapshotTargetFailureValues.StaleExplicitTarget);
            }

            WindowDescriptor? explicitWindow = ResolveExplicitOrAttachedWindowCore(hwnd, attachedWindow: null, liveWindows);
            return explicitWindow is null
                ? new(FailureCode: UiaSnapshotTargetFailureValues.StaleExplicitTarget)
                : new(explicitWindow, UiaSnapshotTargetSourceValues.Explicit, FailureCode: null);
        }

        if (attachedWindow is not null)
        {
            WindowDescriptor? resolvedAttachedWindow = ResolveExplicitOrAttachedWindowCore(explicitHwnd: null, attachedWindow, liveWindows);
            return resolvedAttachedWindow is null
                ? new(FailureCode: UiaSnapshotTargetFailureValues.StaleAttachedTarget)
                : new(resolvedAttachedWindow, UiaSnapshotTargetSourceValues.Attached, FailureCode: null);
        }

        WindowDescriptor[] activeCandidates = liveWindows
            .Where(candidate => candidate.IsForeground)
            .GroupBy(candidate => candidate.Hwnd)
            .Select(group => group.First())
            .Take(2)
            .ToArray();

        return activeCandidates.Length switch
        {
            0 => new(FailureCode: UiaSnapshotTargetFailureValues.MissingTarget),
            1 => new(activeCandidates[0], UiaSnapshotTargetSourceValues.Active, FailureCode: null),
            _ => new(FailureCode: UiaSnapshotTargetFailureValues.AmbiguousActiveTarget),
        };
    }

    private static WindowDescriptor? ResolveExplicitOrAttachedWindowCore(
        long? explicitHwnd,
        WindowDescriptor? attachedWindow,
        IReadOnlyList<WindowDescriptor> liveWindows)
    {
        if (explicitHwnd is long hwnd)
        {
            return liveWindows.FirstOrDefault(candidate => candidate.Hwnd == hwnd);
        }

        if (attachedWindow is null)
        {
            return null;
        }

        if (!WindowIdentityValidator.TryValidateStableIdentity(attachedWindow, out _))
        {
            return null;
        }

        WindowDescriptor? liveCandidate = liveWindows.FirstOrDefault(candidate => candidate.Hwnd == attachedWindow.Hwnd);
        if (liveCandidate is null)
        {
            return null;
        }

        return WindowIdentityValidator.MatchesStableIdentity(liveCandidate, attachedWindow) ? liveCandidate : null;
    }
}
