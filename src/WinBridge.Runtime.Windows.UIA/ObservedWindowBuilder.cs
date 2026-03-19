using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal static class ObservedWindowBuilder
{
    public static ObservedWindowDescriptor Create(
        WindowDescriptor targetWindow,
        AutomationElement root,
        UiaSnapshotNodeData rootData)
    {
        long hwnd = rootData.NativeWindowHandle is long nativeHwnd && nativeHwnd > 0
            ? nativeHwnd
            : targetWindow.Hwnd;
        IntPtr windowHandle = new(hwnd);
        int? processId = GetCachedProcessId(root);

        return new(
            Hwnd: hwnd,
            Title: NormalizeObservedString(rootData.Name),
            ProcessName: processId is int processIdentifier ? TryGetProcessName(processIdentifier) : null,
            ProcessId: processId,
            ThreadId: TryGetThreadId(windowHandle, processId),
            ClassName: NormalizeObservedString(rootData.ClassName),
            Bounds: rootData.BoundingRectangle);
    }

    private static string? NormalizeObservedString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? GetCachedProcessId(AutomationElement automationElement)
    {
        object value = automationElement.GetCachedPropertyValue(AutomationElementIdentifiers.ProcessIdProperty, true);
        return value is int processId && processId > 0 ? processId : null;
    }

    private static int? TryGetThreadId(IntPtr hwnd, int? expectedProcessId)
    {
        uint threadId = GetWindowThreadProcessId(hwnd, out uint processId);
        if (threadId == 0)
        {
            return null;
        }

        if (expectedProcessId is int pid && processId != checked((uint)pid))
        {
            return null;
        }

        return checked((int)threadId);
    }

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
