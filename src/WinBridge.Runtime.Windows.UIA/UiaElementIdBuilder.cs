namespace WinBridge.Runtime.Windows.UIA;

internal static class UiaElementIdBuilder
{
    public static string Create(int[]? runtimeId, string path)
    {
        if (runtimeId is { Length: > 0 })
        {
            return "rid:" + string.Join(".", runtimeId);
        }

        return "path:" + path;
    }
}
