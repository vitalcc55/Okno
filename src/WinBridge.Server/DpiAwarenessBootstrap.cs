using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinBridge.Server;

internal static class DpiAwarenessBootstrap
{
    private const int ProcessPerMonitorDpiAware = 2;
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    public static void EnsurePerMonitorAware()
    {
        if (SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2))
        {
            return;
        }

        int lastError = Marshal.GetLastWin32Error();
        if (GetProcessDpiAwareness(Process.GetCurrentProcess().Handle, out int awareness) == 0
            && awareness == ProcessPerMonitorDpiAware)
        {
            return;
        }

        string message = lastError == 0
            ? "Runtime не смог включить per-monitor DPI awareness до старта MCP host."
            : $"Runtime не смог включить per-monitor DPI awareness до старта MCP host. Win32 error: {new Win32Exception(lastError).Message} ({lastError}).";
        throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr processHandle, out int awareness);
}
