using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

internal enum WgcAcquisitionFailureAction
{
    FallbackToDesktopGdi,
    ThrowToolError,
}

internal static class WgcAcquisitionFailurePolicy
{
    public static WgcAcquisitionFailureAction Evaluate(CaptureScope scope) =>
        scope == CaptureScope.Desktop
            ? WgcAcquisitionFailureAction.FallbackToDesktopGdi
            : WgcAcquisitionFailureAction.ThrowToolError;
}
