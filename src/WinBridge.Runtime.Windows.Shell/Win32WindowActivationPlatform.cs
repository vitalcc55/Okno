using System.Runtime.InteropServices;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class Win32WindowActivationPlatform : IWindowActivationPlatform
{
    private const int SwRestore = 9;

    public bool IsWindow(long hwnd) =>
        IsWindowNative(new IntPtr(hwnd));

    public bool IsIconic(long hwnd) =>
        IsIconicNative(new IntPtr(hwnd));

    public void RestoreWindow(long hwnd)
    {
        _ = ShowWindowAsync(new IntPtr(hwnd), SwRestore);
    }

    public long GetForegroundWindow() =>
        GetForegroundWindowNative().ToInt64();

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    private static extern bool IsWindowNative(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    private static extern bool IsIconicNative(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();
}
