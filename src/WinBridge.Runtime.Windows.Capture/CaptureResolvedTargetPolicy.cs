using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

internal static class CaptureResolvedTargetPolicy
{
    public static CaptureResolvedTarget BuildAuthoritativeWgcTarget(
        CaptureResolvedTarget initialTarget,
        CaptureResolvedTarget? refreshedTarget,
        WgcFrameSize acceptedContentSize)
    {
        CaptureResolvedTarget basis = SelectBasis(initialTarget, refreshedTarget);
        Bounds? frameBounds = basis.Scope == CaptureScope.Window
            ? basis.FrameBounds ?? basis.Bounds
            : null;
        Bounds authoritativeBounds = frameBounds is null
            ? ResizeBounds(basis.Bounds, acceptedContentSize)
            : BuildAuthoritativeWindowRasterBounds(basis.Bounds, frameBounds, acceptedContentSize);
        WindowDescriptor? authoritativeWindow = basis.Window is null
            ? null
            : basis.Window with
            {
                Bounds = frameBounds ?? authoritativeBounds,
                EffectiveDpi = basis.EffectiveDpi ?? basis.Window.EffectiveDpi,
                DpiScale = basis.DpiScale ?? basis.Window.DpiScale,
                MonitorId = basis.Monitor?.Descriptor.MonitorId ?? basis.Window.MonitorId,
                MonitorFriendlyName = basis.Monitor?.Descriptor.FriendlyName ?? basis.Window.MonitorFriendlyName,
            };

        return basis with
        {
            Window = authoritativeWindow,
            Bounds = authoritativeBounds,
            FrameBounds = frameBounds,
        };
    }

    public static WindowDescriptor BuildAuthoritativeWgcProbeWindow(
        CaptureResolvedTarget initialTarget,
        CaptureResolvedTarget? refreshedTarget)
    {
        WindowDescriptor basisWindow = initialTarget.Window
            ?? throw new CaptureOperationException("Runtime не смог материализовать window target для visual probe.");
        Bounds frameBounds = initialTarget.FrameBounds ?? basisWindow.Bounds;

        if (refreshedTarget is not null)
        {
            ValidateWindowRefreshMatchesAcquisitionBasis(initialTarget, refreshedTarget);
        }

        return basisWindow with
        {
            Bounds = frameBounds,
            EffectiveDpi = initialTarget.EffectiveDpi ?? basisWindow.EffectiveDpi,
            DpiScale = initialTarget.DpiScale ?? basisWindow.DpiScale,
            MonitorId = initialTarget.Monitor?.Descriptor.MonitorId ?? basisWindow.MonitorId,
            MonitorFriendlyName = initialTarget.Monitor?.Descriptor.FriendlyName ?? basisWindow.MonitorFriendlyName,
        };
    }

    private static CaptureResolvedTarget SelectBasis(
        CaptureResolvedTarget initialTarget,
        CaptureResolvedTarget? refreshedTarget)
    {
        if (refreshedTarget is null)
        {
            return initialTarget;
        }

        if (initialTarget.Scope == CaptureScope.Window)
        {
            ValidateWindowRefreshMatchesAcquisitionBasis(initialTarget, refreshedTarget);
            return initialTarget;
        }

        string? initialMonitorId = initialTarget.Monitor?.Descriptor.MonitorId;
        string? refreshedMonitorId = refreshedTarget.Monitor?.Descriptor.MonitorId;
        return string.Equals(initialMonitorId, refreshedMonitorId, StringComparison.Ordinal)
            ? refreshedTarget
            : initialTarget;
    }

    private static void ValidateWindowRefreshMatchesAcquisitionBasis(
        CaptureResolvedTarget initialTarget,
        CaptureResolvedTarget refreshedTarget)
    {
        Bounds initialFrameBounds = initialTarget.FrameBounds ?? initialTarget.Bounds;
        Bounds refreshedFrameBounds = refreshedTarget.FrameBounds ?? refreshedTarget.Bounds;

        if (!CaptureReferenceGeometryPolicy.BoundsMatchWithinDrift(initialFrameBounds, refreshedFrameBounds)
            || !CaptureReferenceGeometryPolicy.BoundsMatchWithinDrift(initialTarget.Bounds, refreshedTarget.Bounds))
        {
            throw new CaptureOperationException(
                "Window capture target geometry изменилась после WGC frame acquisition. Runtime не будет публиковать stale captureReference geometry.");
        }

        if (initialTarget.EffectiveDpi != refreshedTarget.EffectiveDpi)
        {
            throw new CaptureOperationException(
                "Window capture target DPI изменился после WGC frame acquisition. Runtime не будет публиковать stale captureReference geometry.");
        }
    }

    private static Bounds ResizeBounds(Bounds basis, WgcFrameSize acceptedContentSize) =>
        new(
            basis.Left,
            basis.Top,
            basis.Left + acceptedContentSize.Width,
            basis.Top + acceptedContentSize.Height);

    private static Bounds BuildAuthoritativeWindowRasterBounds(
        Bounds rasterOriginBasis,
        Bounds frameBounds,
        WgcFrameSize acceptedContentSize)
    {
        if (BoundsEqual(rasterOriginBasis, frameBounds)
            && (acceptedContentSize.Width != frameBounds.Width || acceptedContentSize.Height != frameBounds.Height))
        {
            throw new CaptureOperationException("Window capture не смог доказать raster/content origin внутри frame bounds.");
        }

        if (!ContainsBounds(frameBounds, rasterOriginBasis))
        {
            throw new CaptureOperationException("Window capture получил raster/content bounds вне frame bounds.");
        }

        Bounds rasterBounds = ResizeBounds(rasterOriginBasis, acceptedContentSize);
        if (!ContainsBounds(frameBounds, rasterBounds))
        {
            throw new CaptureOperationException("Window capture получил raster/content size, который выходит за frame bounds.");
        }

        return rasterBounds;
    }

    private static bool BoundsEqual(Bounds left, Bounds right) =>
        left.Left == right.Left
        && left.Top == right.Top
        && left.Right == right.Right
        && left.Bottom == right.Bottom;

    private static bool ContainsBounds(Bounds outer, Bounds inner) =>
        inner.Left >= outer.Left
        && inner.Top >= outer.Top
        && inner.Right <= outer.Right
        && inner.Bottom <= outer.Bottom;
}
