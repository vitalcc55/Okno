using System.Diagnostics;
using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Shell;

public sealed class Win32WindowManager(IMonitorManager monitorManager) : IWindowManager
{
    public IReadOnlyList<WindowDescriptor> ListWindows(bool includeInvisible = false)
    {
        long? foregroundWindowHwnd = GetForegroundWindowNativeHwnd();
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        IReadOnlyList<MonitorInfo> monitors = topology.Monitors;
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
                    ? monitorManager.FindMonitorByHandle(handle, topology)
                    : null;
                if (!WindowDpiReader.TryGetEffectiveDpi(hWnd, out int effectiveDpi))
                {
                    return true;
                }

                windows.Add(
                    new WindowDescriptor(
                        Hwnd: hWnd.ToInt64(),
                        Title: title,
                        ProcessName: threadId == 0 ? null : TryGetProcessName(processId),
                        ProcessId: threadId == 0 ? null : checked((int)processId),
                        ThreadId: threadId == 0 ? null : checked((int)threadId),
                        ClassName: TryGetWindowClassName(hWnd),
                        Bounds: new Bounds(rect.Left, rect.Top, rect.Right, rect.Bottom),
                        IsForeground: foregroundWindowHwnd is long foregroundHwnd && hWnd.ToInt64() == foregroundHwnd,
                        IsVisible: isVisible,
                        EffectiveDpi: effectiveDpi,
                        DpiScale: effectiveDpi / 96.0,
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
        if (success || GetForegroundWindowNativeHwnd() == hwnd)
        {
            return true;
        }

        IntPtr foregroundWindow = GetForegroundWindow();
        uint currentThreadId = GetCurrentThreadId();
        uint targetThreadId = GetWindowThreadProcessId(target, out _);
        uint foregroundThreadId = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;
        try
        {
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);

            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (targetThreadId != 0 && targetThreadId != currentThreadId && targetThreadId != foregroundThreadId)
            {
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            _ = BringWindowToTop(target);
            _ = SetActiveWindow(target);
            _ = SetFocus(target);
            success = SetForegroundWindow(target);
        }
        finally
        {
            if (attachedToTarget)
            {
                _ = AttachThreadInput(currentThreadId, targetThreadId, false);
            }

            if (attachedToForeground)
            {
                _ = AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }

        return success || GetForegroundWindowNativeHwnd() == hwnd;
    }

    private static long? GetForegroundWindowNativeHwnd()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        return foregroundWindow == IntPtr.Zero ? null : foregroundWindow.ToInt64();
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
            _ when TryIsWindowArranged(hWnd) => WindowStateValues.Arranged,
            _ => WindowStateValues.Normal,
        };
    }

    private static bool TryIsWindowArranged(IntPtr hWnd)
    {
        IsWindowArrangedInvoker? invoker = s_isWindowArrangedInvoker.Value;
        return invoker is not null && invoker(hWnd);
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
    private const uint PmNoRemove = 0x0000;
    private static readonly Lazy<IsWindowArrangedInvoker?> s_isWindowArrangedInvoker = new(LoadIsWindowArrangedInvoker);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

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

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    private static IsWindowArrangedInvoker? LoadIsWindowArrangedInvoker()
    {
        try
        {
            if (!NativeLibrary.TryLoad("user32.dll", out IntPtr libraryHandle))
            {
                return null;
            }

            return NativeLibrary.TryGetExport(libraryHandle, "IsWindowArranged", out IntPtr functionPointer)
                ? Marshal.GetDelegateForFunctionPointer<IsWindowArrangedInvoker>(functionPointer)
                : null;
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool IsWindowArrangedInvoker(IntPtr hWnd);
}
