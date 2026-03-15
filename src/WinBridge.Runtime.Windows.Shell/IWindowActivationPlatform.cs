namespace WinBridge.Runtime.Windows.Shell;

public interface IWindowActivationPlatform
{
    bool IsWindow(long hwnd);

    bool IsIconic(long hwnd);

    void RestoreWindow(long hwnd);

    long GetForegroundWindow();
}
