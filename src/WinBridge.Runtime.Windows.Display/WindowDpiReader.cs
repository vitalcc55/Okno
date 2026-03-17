using System.Runtime.InteropServices;

namespace WinBridge.Runtime.Windows.Display;

public static class WindowDpiReader
{
    public static bool TryGetEffectiveDpi(IntPtr hwnd, out int effectiveDpi)
    {
        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            effectiveDpi = 0;
            return false;
        }

        effectiveDpi = checked((int)dpi);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}
