using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class RuntimeGuardServiceTests
{
    [Fact]
    public void GetSnapshotUsesExistingTopologyAndPackageBCapabilityBoundary()
    {
        DisplayTopologySnapshot topology = new(
            Monitors:
            [
                new MonitorInfo(
                    new MonitorDescriptor(
                        MonitorId: "display-source:1:1",
                        FriendlyName: "Primary",
                        GdiDeviceName: @"\\.\DISPLAY1",
                        Bounds: new Bounds(0, 0, 1920, 1080),
                        WorkArea: new Bounds(0, 0, 1920, 1040),
                        IsPrimary: true),
                    CaptureHandle: 11,
                    [11])
            ],
            Diagnostics: new DisplayIdentityDiagnostics(
                IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
                FailedStage: null,
                ErrorCode: null,
                ErrorName: null,
                MessageHuman: "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
                CapturedAtUtc: DateTimeOffset.UtcNow));

        RuntimeGuardRawFacts facts = new(
            DesktopSession: new DesktopSessionProbeResult(InputDesktopAvailable: true, ErrorCode: null),
            SessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 1,
                ActiveConsoleSessionId: 1,
                ConnectState: SessionConnectState.Active,
                ClientProtocolType: 0),
            Token: new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.High,
                IntegrityRid: 0x3000,
                ElevationResolved: true,
                IsElevated: true,
                ElevationType: TokenElevationTypeValue.Full,
                UiAccessResolved: true,
                UiAccess: true));

        RuntimeGuardService service = new(
            new FakeMonitorManager(topology),
            new FakeRuntimeGuardPlatform(facts),
            TimeProvider.System);

        RuntimeGuardAssessment snapshot = service.GetSnapshot();

        Assert.Same(topology, snapshot.Topology);
        Assert.NotEqual(default, snapshot.Readiness.CapturedAtUtc);
        Assert.Equal(4, snapshot.Readiness.Domains.Count);
        Assert.Equal(
            [
                GuardStatusValues.Unknown,
                GuardStatusValues.Unknown,
                GuardStatusValues.Unknown,
                GuardStatusValues.Blocked,
                GuardStatusValues.Blocked,
                GuardStatusValues.Blocked,
            ],
            snapshot.Readiness.Capabilities.Select(item => item.Status).ToArray());
        Assert.Equal(
            [
                CapabilitySummaryValues.Capture,
                CapabilitySummaryValues.Uia,
                CapabilitySummaryValues.Wait,
            ],
            snapshot.Warnings.Select(item => item.Source).ToArray());
        Assert.Equal(
            [
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Clipboard,
                CapabilitySummaryValues.Launch,
            ],
            snapshot.BlockedCapabilities.Select(item => item.Capability).ToArray());
    }

    private sealed class FakeRuntimeGuardPlatform(RuntimeGuardRawFacts facts) : IRuntimeGuardPlatform
    {
        public RuntimeGuardRawFacts Probe() => facts;
    }

    private sealed class FakeMonitorManager(DisplayTopologySnapshot topology) : IMonitorManager
    {
        public DisplayTopologySnapshot GetTopologySnapshot() => topology;

        public MonitorInfo? FindMonitorById(string monitorId, DisplayTopologySnapshot? snapshot = null) => null;

        public MonitorInfo? FindMonitorByHandle(long handle, DisplayTopologySnapshot? snapshot = null) => null;

        public long? GetMonitorHandleForWindow(long hwnd) => null;

        public MonitorInfo? FindMonitorForWindow(long hwnd, DisplayTopologySnapshot? snapshot = null) => null;

        public MonitorInfo? GetPrimaryMonitor(DisplayTopologySnapshot? snapshot = null) =>
            topology.Monitors.Count == 0 ? null : topology.Monitors[0];
    }
}
