namespace WinBridge.Runtime.Windows.Display;

public static class MonitorIdFormatter
{
    public static string FromDisplayConfig(int highPart, uint lowPart, uint targetId)
    {
        uint high = unchecked((uint)highPart);
        return $"displayconfig:{high:x8}{lowPart:x8}:{targetId}";
    }

    public static string FromGdiDeviceName(string gdiDeviceName)
    {
        string token = NormalizeGdiDeviceName(gdiDeviceName).Replace(@"\\.\", string.Empty, StringComparison.OrdinalIgnoreCase);
        return "gdi:" + token.ToLowerInvariant();
    }

    private static string NormalizeGdiDeviceName(string gdiDeviceName) =>
        (gdiDeviceName ?? string.Empty).Trim().ToUpperInvariant();
}
