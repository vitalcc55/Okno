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

    public ActivatedWindowVerificationSnapshot ProbeWindow(long hwnd)
    {
        IntPtr target = new(hwnd);
        bool exists = IsWindowNative(target);
        if (!exists)
        {
            return new(false, null, null, null, false, false);
        }

        uint threadId = GetWindowThreadProcessId(target, out uint processId);
        return new(
            Exists: true,
            ProcessId: threadId == 0 ? null : checked((int)processId),
            ThreadId: threadId == 0 ? null : checked((int)threadId),
            ClassName: TryGetWindowClassName(target),
            IsForeground: GetForegroundWindowNative() == target,
            IsMinimized: IsIconicNative(target));
    }

    private static string? TryGetWindowClassName(IntPtr hwnd)
    {
        char[] buffer = new char[256];
        int length = GetClassNameNative(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    private static extern bool IsWindowNative(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "IsIconic")]
    private static extern bool IsIconicNative(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameNative(IntPtr hwnd, [Out] char[] className, int maxCount);
}
