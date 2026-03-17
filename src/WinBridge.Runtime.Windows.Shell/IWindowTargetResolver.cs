using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowTargetResolver
{
    WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow);

    WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow);
}
