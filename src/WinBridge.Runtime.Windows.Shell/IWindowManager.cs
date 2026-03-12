using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowManager
{
    IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false);

    WindowDescriptor? FindWindow(WindowSelector selector);

    bool TryFocus(long hwnd);
}
