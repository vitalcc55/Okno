namespace WinBridge.Runtime.Contracts;

internal static class CaptureReferenceGeometryPolicy
{
    internal const int AllowedWindowGeometryEdgeDrift = 1;

    public static InputCaptureReference CreateCopyThroughReference(
        Bounds bounds,
        int pixelWidth,
        int pixelHeight,
        int? effectiveDpi,
        DateTimeOffset capturedAtUtc,
        Bounds? frameBounds = null)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        InputCaptureReference reference = new(
            ToInputBounds(bounds),
            pixelWidth,
            pixelHeight,
            effectiveDpi,
            capturedAtUtc,
            frameBounds is null ? null : ToInputBounds(frameBounds));

        if (!TryCreateGeometryBasis(reference, out _))
        {
            throw new InvalidOperationException("Capture reference geometry must satisfy the shared capture/input contract.");
        }

        return reference;
    }

    public static bool TryCreateGeometryBasis(
        InputCaptureReference captureReference,
        out CaptureReferenceGeometryBasis? basis)
    {
        basis = null;
        if (captureReference.Bounds is not InputBounds captureBounds)
        {
            return false;
        }

        InputBounds frameBounds = captureReference.FrameBounds ?? captureBounds;
        if (!TryGetExtent(captureBounds.Left, captureBounds.Right, out long captureWidth)
            || !TryGetExtent(captureBounds.Top, captureBounds.Bottom, out long captureHeight)
            || !TryGetExtent(frameBounds.Left, frameBounds.Right, out long frameWidth)
            || !TryGetExtent(frameBounds.Top, frameBounds.Bottom, out long frameHeight))
        {
            return false;
        }

        if (captureWidth != captureReference.PixelWidth
            || captureHeight != captureReference.PixelHeight)
        {
            return false;
        }

        if (captureBounds.Left < frameBounds.Left
            || captureBounds.Top < frameBounds.Top
            || captureBounds.Right > frameBounds.Right
            || captureBounds.Bottom > frameBounds.Bottom)
        {
            return false;
        }

        if (!TryGetContentOffset(captureBounds.Left, frameBounds.Left, out int contentOffsetX)
            || !TryGetContentOffset(captureBounds.Top, frameBounds.Top, out int contentOffsetY))
        {
            return false;
        }

        basis = new(
            CaptureBounds: captureBounds,
            FrameBounds: frameBounds,
            CaptureWidth: captureWidth,
            CaptureHeight: captureHeight,
            FrameWidth: frameWidth,
            FrameHeight: frameHeight,
            ContentOffsetX: contentOffsetX,
            ContentOffsetY: contentOffsetY,
            EffectiveDpi: captureReference.EffectiveDpi);
        return true;
    }

    public static bool BoundsMatchWithinDrift(Bounds left, Bounds right) =>
        EdgeMatchesWithinDrift(left.Left, right.Left)
        && EdgeMatchesWithinDrift(left.Top, right.Top)
        && EdgeMatchesWithinDrift(left.Right, right.Right)
        && EdgeMatchesWithinDrift(left.Bottom, right.Bottom);

    public static bool BoundsMatchWithinDrift(InputBounds left, Bounds right) =>
        EdgeMatchesWithinDrift(left.Left, right.Left)
        && EdgeMatchesWithinDrift(left.Top, right.Top)
        && EdgeMatchesWithinDrift(left.Right, right.Right)
        && EdgeMatchesWithinDrift(left.Bottom, right.Bottom);

    private static InputBounds ToInputBounds(Bounds bounds) =>
        new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

    private static bool TryGetExtent(int startEdge, int endEdge, out long extent)
    {
        extent = (long)endEdge - startEdge;
        return extent > 0;
    }

    private static bool TryGetContentOffset(int contentEdge, int frameEdge, out int offset)
    {
        long value = (long)contentEdge - frameEdge;
        if (value is < 0 or > int.MaxValue)
        {
            offset = 0;
            return false;
        }

        offset = (int)value;
        return true;
    }

    private static bool EdgeMatchesWithinDrift(int left, int right)
    {
        long delta = (long)left - right;
        return delta is >= -AllowedWindowGeometryEdgeDrift and <= AllowedWindowGeometryEdgeDrift;
    }
}

internal sealed record CaptureReferenceGeometryBasis(
    InputBounds CaptureBounds,
    InputBounds FrameBounds,
    long CaptureWidth,
    long CaptureHeight,
    long FrameWidth,
    long FrameHeight,
    int ContentOffsetX,
    int ContentOffsetY,
    int? EffectiveDpi);
