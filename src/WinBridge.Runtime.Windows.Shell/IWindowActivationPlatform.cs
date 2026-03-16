namespace WinBridge.Runtime.Windows.Shell;

public readonly record struct ActivatedWindowVerificationSnapshot(
    bool Exists,
    int? ProcessId,
    int? ThreadId,
    string? ClassName,
    bool IsForeground,
    bool IsMinimized);

public interface IWindowActivationPlatform
{
    bool IsWindow(long hwnd);

    bool IsIconic(long hwnd);

    void RestoreWindow(long hwnd);

    long GetForegroundWindow();

    ActivatedWindowVerificationSnapshot ProbeWindow(long hwnd);
}
