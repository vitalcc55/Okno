using System.Diagnostics;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class Win32WindowManager(IMonitorManager monitorManager) : IWindowManager
{
    public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false)
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        IReadOnlyList<MonitorInfo> monitors = monitorManager.ListMonitors();
        List<WindowDescriptor> windows = new();

        _ = EnumWindows(
            (hWnd, _) =>
            {
                bool isVisible = IsWindowVisible(hWnd);
                if (!includeInvisible && !isVisible)
                {
                    return true;
                }

                string title = GetWindowTitle(hWnd);
                if (!includeInvisible && string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                if (!GetWindowRect(hWnd, out RECT rect))
                {
                    return true;
                }

                uint threadId = GetWindowThreadProcessId(hWnd, out uint processId);
                long? monitorHandle = monitorManager.GetMonitorHandleForWindow(hWnd.ToInt64());
                MonitorInfo? monitor = monitorHandle is long handle
                    ? monitorManager.FindMonitorByHandle(handle, monitors)
                    : null;
                windows.Add(
                    new WindowDescriptor(
                        Hwnd: hWnd.ToInt64(),
                        Title: title,
                        ProcessName: threadId == 0 ? null : TryGetProcessName(processId),
                        ProcessId: threadId == 0 ? null : checked((int)processId),
                        ThreadId: threadId == 0 ? null : checked((int)threadId),
                        ClassName: TryGetWindowClassName(hWnd),
                        Bounds: new Bounds(rect.Left, rect.Top, rect.Right, rect.Bottom),
                        IsForeground: hWnd == foregroundWindow,
                        IsVisible: isVisible,
                        WindowState: GetWindowState(hWnd),
                        MonitorId: monitor?.Descriptor.MonitorId,
                        MonitorFriendlyName: monitor?.Descriptor.FriendlyName));

                return true;
            },
            IntPtr.Zero);

        return windows
            .OrderByDescending(window => window.IsForeground)
            .ThenByDescending(window => window.IsVisible)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WindowDescriptor? FindWindow(WindowSelector selector)
    {
        selector.Validate();

        IEnumerable<WindowDescriptor> query = ListWindows(includeInvisible: true);

        if (selector.Hwnd is long hwnd)
        {
            query = query.Where(window => window.Hwnd == hwnd);
        }

        if (!string.IsNullOrWhiteSpace(selector.ProcessName))
        {
            query = query.Where(
                window => string.Equals(
                    window.ProcessName,
                    selector.ProcessName,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(selector.TitlePattern))
        {
            query = query.Where(window => TitlePatternMatcher.IsMatch(window.Title, selector.TitlePattern));
        }

        WindowDescriptor[] matches = query.Take(2).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "По указанному селектору найдено несколько окон; уточни hwnd, titlePattern или processName."),
        };
    }

    public bool TryFocus(long hwnd)
    {
        IntPtr target = new(hwnd);
        if (!IsWindow(target))
        {
            return false;
        }

        bool success = SetForegroundWindow(target);
        return success || GetForegroundWindow() == target;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        _ = GetWindowText(hWnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    private static string? TryGetProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetWindowClassName(IntPtr hWnd)
    {
        char[] buffer = new char[256];
        int length = GetClassName(hWnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string GetWindowState(IntPtr hWnd)
    {
        WINDOWPLACEMENT placement = new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hWnd, ref placement))
        {
            return WindowStateValues.Unknown;
        }

        return placement.showCmd switch
        {
            SwShowMaximized => WindowStateValues.Maximized,
            SwShowMinimized or SwMinimize or SwShowMinNoActive => WindowStateValues.Minimized,
            _ when IsWindowArranged(hWnd) => WindowStateValues.Arranged,
            _ => WindowStateValues.Normal,
        };
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    private const int SwShowMaximized = 3;
    private const int SwShowMinimized = 2;
    private const int SwMinimize = 6;
    private const int SwShowMinNoActive = 7;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool IsWindowArranged(IntPtr hWnd);
}
