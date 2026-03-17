using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class WindowTargetResolver(IWindowManager windowManager) : IWindowTargetResolver
{
    public WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow)
    {
        IReadOnlyList<WindowDescriptor> liveWindows = windowManager.ListWindows(includeInvisible: true);

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
}
