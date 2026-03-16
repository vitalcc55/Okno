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

        WindowDescriptor? liveCandidate = liveWindows.FirstOrDefault(candidate => candidate.Hwnd == attachedWindow.Hwnd);
        if (liveCandidate is null)
        {
            return null;
        }

        return MatchesAttachedWindowIdentity(liveCandidate, attachedWindow) ? liveCandidate : null;
    }

    private static bool MatchesAttachedWindowIdentity(
        WindowDescriptor liveCandidate,
        WindowDescriptor attachedWindow)
    {
        bool processIdCompatible = liveCandidate.ProcessId is null
            || attachedWindow.ProcessId is null
            || liveCandidate.ProcessId == attachedWindow.ProcessId;
        bool threadIdCompatible = liveCandidate.ThreadId is null
            || attachedWindow.ThreadId is null
            || liveCandidate.ThreadId == attachedWindow.ThreadId;
        bool classNameCompatible = string.IsNullOrWhiteSpace(liveCandidate.ClassName)
            || string.IsNullOrWhiteSpace(attachedWindow.ClassName)
            || string.Equals(liveCandidate.ClassName, attachedWindow.ClassName, StringComparison.Ordinal);

        return processIdCompatible && threadIdCompatible && classNameCompatible;
    }
}
