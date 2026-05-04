// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Capture;
using WinBridge.Runtime.Windows.Display;
using Windows.Graphics.Imaging;

namespace WinBridge.Runtime.Tests;

public sealed class CaptureResolvedTargetPolicyTests
{
    [Fact]
    public void BuildAuthoritativeWgcWindowTargetKeepsAcquisitionBasisWhenRefreshMatches()
    {
        Bounds frameBounds = new(100, 200, 1100, 900);
        Bounds rasterBounds = new(112, 208, 912, 808);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:refreshed:2",
            monitorName: "Refreshed monitor",
            frameBounds: frameBounds);

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        Assert.Equal(rasterBounds, authoritativeTarget.Bounds);
        Assert.Equal(frameBounds, authoritativeTarget.FrameBounds);
        Assert.Equal(96, authoritativeTarget.EffectiveDpi);
        Assert.Equal(1.0, authoritativeTarget.DpiScale);
        Assert.Equal("display-source:initial:1", authoritativeTarget.Monitor?.Descriptor.MonitorId);
        Assert.Equal("Initial monitor", authoritativeTarget.Monitor?.Descriptor.FriendlyName);
        Assert.Equal(frameBounds, authoritativeTarget.Window?.Bounds);
        Assert.Equal("display-source:initial:1", authoritativeTarget.Window?.MonitorId);
        Assert.Equal("Initial monitor", authoritativeTarget.Window?.MonitorFriendlyName);
        Assert.Equal(CaptureReferenceEligibility.Eligible, authoritativeTarget.CaptureReferenceEligibility);
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetPublishesCaptureTimeFrameBasis()
    {
        Bounds frameBounds = new(100, 200, 1100, 900);
        Bounds rasterBounds = new(112, 208, 912, 808);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: new Bounds(113, 208, 913, 808),
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:refreshed:2",
            monitorName: "Refreshed monitor",
            frameBounds: new Bounds(101, 200, 1101, 900));

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        Assert.Equal(rasterBounds, authoritativeTarget.Bounds);
        Assert.Equal(frameBounds, authoritativeTarget.FrameBounds);
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetUsesRasterOriginInsideFrame()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        Assert.Equal(rasterBounds, authoritativeTarget.Bounds);
        Assert.Equal(frameBounds, authoritativeTarget.FrameBounds);
        Assert.Equal(frameBounds, authoritativeTarget.Window?.Bounds);
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetRejectsPostCaptureFrameDrift()
    {
        Bounds initialFrameBounds = new(100, 120, 920, 740);
        Bounds initialRasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: initialFrameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: new Bounds(100, 120, 960, 780));

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
                initialTarget,
                refreshedTarget,
                new WgcFrameSize(initialRasterBounds.Width, initialRasterBounds.Height)));
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetRejectsPostCaptureRasterDrift()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds initialRasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: new Bounds(120, 136, 908, 708),
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
                initialTarget,
                refreshedTarget,
                new WgcFrameSize(initialRasterBounds.Width, initialRasterBounds.Height)));
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetRejectsPostCaptureDpiDrift()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 144,
            dpiScale: 1.5,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
                initialTarget,
                refreshedTarget,
                new WgcFrameSize(rasterBounds.Width, rasterBounds.Height)));
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetRejectsPostCaptureIdentityDrift()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds,
            processId: 999,
            threadId: 888,
            className: "ReplacementWindow");

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
                initialTarget,
                refreshedTarget,
                new WgcFrameSize(rasterBounds.Width, rasterBounds.Height)));
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetAllowsMissingIdentityWhenGeometryStillMatches()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds,
            processId: null,
            threadId: null,
            className: null);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds,
            processId: null,
            threadId: null,
            className: null);

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        Assert.Equal(rasterBounds, authoritativeTarget.Bounds);
        Assert.Null(authoritativeTarget.Window?.ProcessId);
        Assert.Null(authoritativeTarget.Window?.ThreadId);
        Assert.Null(authoritativeTarget.Window?.ClassName);
        Assert.Equal(CaptureReferenceEligibility.ObserveOnly, authoritativeTarget.CaptureReferenceEligibility);
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetKeepsObserveOnlyWhenRefreshCannotConfirmInitialIdentity()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds,
            processId: null,
            threadId: null,
            className: null);

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        Assert.Equal(CaptureReferenceEligibility.ObserveOnly, authoritativeTarget.CaptureReferenceEligibility);
        Assert.Null(CaptureReferencePublisher.TryCreate(
            CaptureScope.Window,
            authoritativeTarget,
            rasterBounds.Width,
            rasterBounds.Height,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CaptureReferencePublisherPublishesOnlyEligibleWindowTarget()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        InputCaptureReference? captureReference = CaptureReferencePublisher.TryCreate(
            CaptureScope.Window,
            authoritativeTarget,
            rasterBounds.Width,
            rasterBounds.Height,
            DateTimeOffset.UtcNow);

        Assert.NotNull(captureReference);
        Assert.Equal(authoritativeTarget.Window?.Hwnd, captureReference.TargetIdentity?.Hwnd);
        Assert.Equal(authoritativeTarget.Window?.ProcessId, captureReference.TargetIdentity?.ProcessId);
        Assert.Equal(authoritativeTarget.Window?.ThreadId, captureReference.TargetIdentity?.ThreadId);
        Assert.Equal(authoritativeTarget.Window?.ClassName, captureReference.TargetIdentity?.ClassName);
    }

    [Fact]
    public void CaptureWindowSnapshotPolicyUsesLiveIdentityForPostCaptureRefresh()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        WindowDescriptor liveReplacementWindow = initialTarget.Window! with
        {
            ProcessId = 999,
            ThreadId = 888,
            ClassName = "ReplacementWindow",
        };
        WindowDescriptor refreshedWindow = CaptureWindowSnapshotPolicy.BuildRefreshedWindowSnapshot(
            initialTarget.Window!,
            liveReplacementWindow,
            frameBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            initialTarget.Monitor);
        CaptureResolvedTarget refreshedTarget = initialTarget with { Window = refreshedWindow };

        Assert.Equal(999, refreshedWindow.ProcessId);
        Assert.Equal(888, refreshedWindow.ThreadId);
        Assert.Equal("ReplacementWindow", refreshedWindow.ClassName);
        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
                initialTarget,
                refreshedTarget,
                new WgcFrameSize(rasterBounds.Width, rasterBounds.Height)));
    }

    [Fact]
    public void CaptureWindowSnapshotPolicyDowngradesMissingLiveRefreshToObserveOnly()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds rasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: rasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        WindowDescriptor refreshedWindow = CaptureWindowSnapshotPolicy.BuildRefreshedWindowSnapshot(
            initialTarget.Window!,
            liveWindow: null,
            frameBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            initialTarget.Monitor);
        CaptureResolvedTarget refreshedTarget = initialTarget with { Window = refreshedWindow };

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget,
            new WgcFrameSize(rasterBounds.Width, rasterBounds.Height));

        Assert.Null(refreshedWindow.ProcessId);
        Assert.Null(refreshedWindow.ThreadId);
        Assert.Null(refreshedWindow.ClassName);
        Assert.Equal(CaptureReferenceEligibility.ObserveOnly, authoritativeTarget.CaptureReferenceEligibility);
        Assert.Null(CaptureReferencePublisher.TryCreate(
            CaptureScope.Window,
            authoritativeTarget,
            rasterBounds.Width,
            rasterBounds.Height,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BuildAuthoritativeWgcProbeWindowRejectsPostCaptureRasterDriftInsideFrame()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds initialRasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: new Bounds(120, 136, 908, 708),
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcProbeWindow(
                initialTarget,
                refreshedTarget));
    }

    [Fact]
    public void BuildAuthoritativeWgcProbeWindowRejectsPostCaptureFrameDrift()
    {
        Bounds initialFrameBounds = new(100, 120, 920, 740);
        Bounds initialRasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: initialFrameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: new Bounds(100, 120, 960, 780));

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcProbeWindow(
                initialTarget,
                refreshedTarget));
    }

    [Fact]
    public void BuildAuthoritativeWgcProbeWindowRejectsPostCaptureIdentityDrift()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        Bounds initialRasterBounds = new(112, 128, 900, 700);
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds);
        CaptureResolvedTarget refreshedTarget = CreateWindowTarget(
            bounds: initialRasterBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: frameBounds,
            processId: 999,
            threadId: 888,
            className: "ReplacementWindow");

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcProbeWindow(
                initialTarget,
                refreshedTarget));
    }

    [Fact]
    public void BuildAuthoritativeWgcWindowTargetRejectsUnprovenRasterOriginInsideFrame()
    {
        Bounds frameBounds = new(100, 120, 920, 740);
        CaptureResolvedTarget target = CreateWindowTarget(
            bounds: frameBounds,
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor") with
            {
                FrameBounds = frameBounds,
            };

        Assert.Throws<CaptureOperationException>(
            () => CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
                target,
                refreshedTarget: null,
                new WgcFrameSize(788, 572)));
    }

    [Fact]
    public void BuildAuthoritativeWgcTargetFallsBackToInitialTargetWhenRefreshUnavailable()
    {
        CaptureResolvedTarget initialTarget = CreateWindowTarget(
            bounds: new Bounds(10, 20, 330, 200),
            effectiveDpi: 96,
            dpiScale: 1.0,
            monitorId: "display-source:initial:1",
            monitorName: "Initial monitor",
            frameBounds: new Bounds(10, 20, 350, 220));

        CaptureResolvedTarget authoritativeTarget = CaptureResolvedTargetPolicy.BuildAuthoritativeWgcTarget(
            initialTarget,
            refreshedTarget: null,
            new WgcFrameSize(320, 180));

        Assert.Equal(new Bounds(10, 20, 330, 200), authoritativeTarget.Bounds);
        Assert.Equal(96, authoritativeTarget.EffectiveDpi);
        Assert.Equal(1.0, authoritativeTarget.DpiScale);
        Assert.Equal("display-source:initial:1", authoritativeTarget.Monitor?.Descriptor.MonitorId);
        Assert.Equal(new Bounds(10, 20, 350, 220), authoritativeTarget.Window?.Bounds);
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
        Assert.Null(authoritativeTarget.FrameBounds);
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
        Assert.Null(authoritativeTarget.FrameBounds);
    }

    [Fact]
    public void WgcCaptureOutcomeDetachesBitmapBeforeDisposal()
    {
        using SoftwareBitmap bitmap = new(BitmapPixelFormat.Bgra8, 2, 3, BitmapAlphaMode.Premultiplied);
        WgcCaptureOutcome outcome = new(bitmap, new WgcFrameSize(2, 3));

        SoftwareBitmap detached = outcome.DetachBitmap();
        outcome.Dispose();

        Assert.Same(bitmap, detached);
        Assert.Equal(2, detached.PixelWidth);
        Assert.Throws<ObjectDisposedException>(() => { _ = outcome.Bitmap; });
    }

    private static CaptureResolvedTarget CreateWindowTarget(
        Bounds bounds,
        int effectiveDpi,
        double dpiScale,
        string monitorId,
        string monitorName,
        Bounds? frameBounds = null,
        int? processId = 123,
        int? threadId = 456,
        string? className = "OknoWindow")
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
            ProcessId: processId,
            ThreadId: threadId,
            ClassName: className,
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
            Monitor: monitor,
            FrameBounds: frameBounds);
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
