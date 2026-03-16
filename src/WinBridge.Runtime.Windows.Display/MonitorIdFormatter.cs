namespace WinBridge.Runtime.Windows.Display;

public static class MonitorIdFormatter
{
    public static string FromDisplaySource(int highPart, uint lowPart, uint sourceId)
    {
        uint high = unchecked((uint)highPart);
        return $"display-source:{high:x8}{lowPart:x8}:{sourceId}";
    }

    public static string FromGdiDeviceName(string gdiDeviceName)
    {
        string token = NormalizeGdiDeviceName(gdiDeviceName).Replace(@"\\.\", string.Empty, StringComparison.OrdinalIgnoreCase);
        return "gdi:" + token.ToLowerInvariant();
    }

    private static string NormalizeGdiDeviceName(string gdiDeviceName) =>
        (gdiDeviceName ?? string.Empty).Trim().ToUpperInvariant();
}
