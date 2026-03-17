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
        Bounds authoritativeBounds = ResizeBounds(basis.Bounds, acceptedContentSize);
        WindowDescriptor? authoritativeWindow = basis.Window is null
            ? null
            : basis.Window with
            {
                Bounds = authoritativeBounds,
                EffectiveDpi = basis.EffectiveDpi ?? basis.Window.EffectiveDpi,
                DpiScale = basis.DpiScale ?? basis.Window.DpiScale,
                MonitorId = basis.Monitor?.Descriptor.MonitorId ?? basis.Window.MonitorId,
                MonitorFriendlyName = basis.Monitor?.Descriptor.FriendlyName ?? basis.Window.MonitorFriendlyName,
            };

        return basis with
        {
            Window = authoritativeWindow,
            Bounds = authoritativeBounds,
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
            return refreshedTarget;
        }

        string? initialMonitorId = initialTarget.Monitor?.Descriptor.MonitorId;
        string? refreshedMonitorId = refreshedTarget.Monitor?.Descriptor.MonitorId;
        return string.Equals(initialMonitorId, refreshedMonitorId, StringComparison.Ordinal)
            ? refreshedTarget
            : initialTarget;
    }

    private static Bounds ResizeBounds(Bounds basis, WgcFrameSize acceptedContentSize) =>
        new(
            basis.Left,
            basis.Top,
            basis.Left + acceptedContentSize.Width,
            basis.Top + acceptedContentSize.Height);
}
