namespace WinBridge.Runtime.Contracts;

public enum CaptureScope
{
    Window,
    Desktop,
}

public static class CaptureScopeExtensions
{
    public static bool TryParse(string? value, out CaptureScope scope)
    {
        if (string.Equals(value, "window", StringComparison.OrdinalIgnoreCase))
        {
            scope = CaptureScope.Window;
            return true;
        }

        if (string.Equals(value, "desktop", StringComparison.OrdinalIgnoreCase))
        {
            scope = CaptureScope.Desktop;
            return true;
        }

        scope = default;
        return false;
    }

    public static string ToContractValue(this CaptureScope scope) =>
        scope switch
        {
            CaptureScope.Window => "window",
            CaptureScope.Desktop => "desktop",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
}
