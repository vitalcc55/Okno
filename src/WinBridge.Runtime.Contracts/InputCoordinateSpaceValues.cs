namespace WinBridge.Runtime.Contracts;

public static class InputCoordinateSpaceValues
{
    public const string CapturePixels = "capture_pixels";
    public const string Screen = "screen";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            CapturePixels,
            Screen,
        };
}
