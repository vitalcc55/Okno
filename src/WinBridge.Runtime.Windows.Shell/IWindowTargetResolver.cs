using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowTargetResolver
{
    WindowDescriptor? ResolveExplicitOrAttachedWindow(long? explicitHwnd, WindowDescriptor? attachedWindow);

    WindowDescriptor? ResolveLiveWindowByIdentity(WindowDescriptor expectedWindow);

    UiaSnapshotTargetResolution ResolveUiaSnapshotTarget(long? explicitHwnd, WindowDescriptor? attachedWindow);

    InputTargetResolution ResolveInputTarget(long? explicitHwnd, WindowDescriptor? attachedWindow);

    WaitTargetResolution ResolveWaitTarget(long? explicitHwnd, WindowDescriptor? attachedWindow);
}
