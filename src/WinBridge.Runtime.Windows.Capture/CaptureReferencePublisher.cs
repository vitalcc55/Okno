using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

internal static class CaptureReferencePublisher
{
    public static InputCaptureReference? TryCreate(
        CaptureScope scope,
        CaptureResolvedTarget target,
        int pixelWidth,
        int pixelHeight,
        DateTimeOffset capturedAtUtc)
    {
        if (scope != CaptureScope.Window
            || target.CaptureReferenceEligibility != CaptureReferenceEligibility.Eligible)
        {
            return null;
        }

        return CaptureReferenceGeometryPolicy.TryCreateCopyThroughReference(
            target.Bounds,
            pixelWidth,
            pixelHeight,
            target.EffectiveDpi,
            capturedAtUtc,
            target.FrameBounds,
            target.Window,
            out InputCaptureReference? captureReference)
            ? captureReference
            : null;
    }
}
