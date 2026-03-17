using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class CaptureResolvedTargetPolicyTests
{
    [Fact]
    public void BuildAuthoritativeWgcTargetUsesRefreshedTargetMetadataAndAcceptedContentSize()
    {
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: new Bounds(10, 20, 210, 220),
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor");
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: new Bounds(100, 200, 1100, 900),
            effectiveDpi: 144,
            dpiScale: 1.5,
            monitorId: "display-source:refreshed:2",
            monitorName: "Refreshed monitor");

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(800, 600));

        Assert.Equal(new Bounds(100, 200, 900, 800), authoritativeTarget.Bounds);
        Assert.Equal(144, authoritativeTarget.EffectiveDpi);
        Assert.Equal(1.5, authoritativeTarget.DpiScale);
        Assert.Equal("display-source:refreshed:2", authoritativeTarget.Monitor?.Descriptor.MonitorId);
        Assert.Equal("Refreshed monitor", authoritativeTarget.Monitor?.Descriptor.FriendlyName);
        Assert.Equal(new Bounds(100, 200, 900, 800), authoritativeTarget.Window?.Bounds);
        Assert.Equal("display-source:refreshed:2", authoritativeTarget.Window?.MonitorId);
        Assert.Equal("Refreshed monitor", authoritativeTarget.Window?.MonitorFriendlyName);
    }

    [Fact]
    public void BuildAuthoritativeWgcTargetFallsBackToInitialTargetWhenRefreshUnavailable()
    {
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: new Bounds(10, 20, 210, 220),
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor");

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget: null,
            new WgcFrameSize(320, 180));

        Assert.Equal(new Bounds(10, 20, 330, 200), authoritativeTarget.Bounds);
        Assert.Equal(96, authoritativeTarget.EffectiveDpi);
        Assert.Equal(1.0, authoritativeTarget.DpiScale);
        Assert.Equal("display-source:initial:1", authoritativeTarget.Monitor?.Descriptor.MonitorId);
        Assert.Equal(new Bounds(10, 20, 330, 200), authoritativeTarget.Window?.Bounds);
    }

    [Fact]
    public void BuildAuthoritativeWgcTargetKeepsInitialDesktopMonitorIdentityWhenRefreshResolvesDifferentMonitor()
    {
        CaptureResolvedTarget initialTarget = CreateDesktopTarget(
            bounds: new Bounds(0, 0, 1920, 1080),
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor");
        CaptureResolvedTarget refreshedTarget = CreateDesktopTarget(
            bounds: new Bounds(1920, 0, 3840, 1080),
            monitorId: "display-source:refreshed:2",
            monitorName: "Refreshed monitor");

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(1600, 900));

        Assert.Equal("display-source:initial:1", authoritativeTarget.Monitor?.Descriptor.MonitorId);
        Assert.Equal("Initial monitor", authoritativeTarget.Monitor?.Descriptor.FriendlyName);
        Assert.Equal(new Bounds(0, 0, 1600, 900), authoritativeTarget.Bounds);
        Assert.Equal("display-source:initial:1", authoritativeTarget.Window?.MonitorId);
    }

    [Fact]
    public void BuildAuthoritativeWgcTargetUsesRefreshedDesktopMetadataWhenMonitorIdentityMatches()
    {
        CaptureResolvedTarget initialTarget = CreateDesktopTarget(
            bounds: new Bounds(0, 0, 1920, 1080),
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor");
        CaptureResolvedTarget refreshedTarget = CreateDesktopTarget(
            bounds: new Bounds(50, 40, 1650, 940),
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor");

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(1600, 900));

        Assert.Equal("display-source:initial:1", authoritativeTarget.Monitor?.Descriptor.MonitorId);
        Assert.Equal(new Bounds(50, 40, 1650, 940), authoritativeTarget.Bounds);
    }

    private static CaptureResolvedTarget CreateWindowTarget(
        Bounds bounds,
        int effectiveDpi,
        double dpiScale,
        string monitorId,
        string monitorName)
    {
        MonitorInfo monitor = new(
            new MonitorDescriptor(
                monitorId,
                monitorName,
                @"\\.\DISPLAY1",
                new Bounds(bounds.Left, bounds.Top, bounds.Left + 1920, bounds.Top + 1080),
                new Bounds(bounds.Left, bounds.Top, bounds.Left + 1920, bounds.Top + 1040),
                IsPrimary: true),
            CaptureHandle: 501,
            Handles: [501]);

        WindowDescriptor window = new(
            Hwnd: 100,
            Title: "Test window",
            ProcessName: "okno-tests",
            ProcessId: 123,
            ThreadId: 456,
            ClassName: "OknoWindow",
            Bounds: bounds,
            IsForeground: true,
            IsVisible: true,
            EffectiveDpi: effectiveDpi,
            DpiScale: dpiScale,
            MonitorId: monitorId,
            MonitorFriendlyName: monitorName);

        return new CaptureResolvedTarget(
            Scope: CaptureScope.Window,
            TargetKind: "window",
            Window: window,
            Bounds: bounds,
            CoordinateSpace: CaptureCoordinateSpaceValues.PhysicalPixels,
            EffectiveDpi: effectiveDpi,
            DpiScale: dpiScale,
            Monitor: monitor);
    }

    private static CaptureResolvedTarget CreateDesktopTarget(
        Bounds bounds,
        string monitorId,
        string monitorName)
    {
        MonitorInfo monitor = new(
            new MonitorDescriptor(
                monitorId,
                monitorName,
                @"\\.\DISPLAY1",
                bounds,
                bounds,
                IsPrimary: true),
            CaptureHandle: 501,
            Handles: [501]);

        WindowDescriptor window = new(
            Hwnd: 100,
            Title: "Desktop source window",
            ProcessName: "okno-tests",
            ProcessId: 123,
            ThreadId: 456,
            ClassName: "OknoWindow",
            Bounds: bounds,
            IsForeground: true,
            IsVisible: true,
            MonitorId: monitorId,
            MonitorFriendlyName: monitorName);

        return new CaptureResolvedTarget(
            Scope: CaptureScope.Desktop,
            TargetKind: "monitor",
            Window: window,
            Bounds: bounds,
            CoordinateSpace: CaptureCoordinateSpaceValues.PhysicalPixels,
            EffectiveDpi: null,
            DpiScale: null,
            Monitor: monitor);
    }
}
