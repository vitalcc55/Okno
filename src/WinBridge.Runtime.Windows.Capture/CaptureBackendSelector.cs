using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

internal enum CaptureBackend
{
    WindowsGraphicsCapture,
    DesktopGdiFallback,
    Unsupported,
}

internal static class CaptureBackendSelector
{
    public static CaptureBackend Select(CaptureScope scope, bool isWindowsGraphicsCaptureSupported) =>
        scope switch
        {
            CaptureScope.Window when isWindowsGraphicsCaptureSupported => CaptureBackend.WindowsGraphicsCapture,
            CaptureScope.Window => CaptureBackend.Unsupported,
            CaptureScope.Desktop when isWindowsGraphicsCaptureSupported => CaptureBackend.WindowsGraphicsCapture,
            CaptureScope.Desktop => CaptureBackend.DesktopGdiFallback,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
}
