using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class RuntimeGuardPolicyTests
{
    [Fact]
    public void BuildDomainsMarksDesktopSessionBlockedWhenInputDesktopUnavailable()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            desktopSession: new DesktopSessionProbeResult(
                InputDesktopAvailable: false,
                ErrorCode: 5,
                DesktopNameResolved: false,
                DesktopName: null));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.DesktopSession);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.InputDesktopUnavailable, reason.Code);
        Assert.Equal(GuardSeverityValues.Blocked, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksDesktopSessionBlockedWhenSessionDisconnected()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 1,
                ActiveConsoleSessionId: 1,
                ConnectState: SessionConnectState.Disconnected,
                ClientProtocolType: 0));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.DesktopSession);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.SessionNotInteractive, reason.Code);
        Assert.Equal(GuardSeverityValues.Blocked, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksDesktopSessionBlockedWhenInputDesktopIsNotDefault()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            desktopSession: new DesktopSessionProbeResult(
                InputDesktopAvailable: true,
                ErrorCode: null,
                DesktopNameResolved: true,
                DesktopName: "Winlogon"));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.DesktopSession);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.InputDesktopNonDefault, reason.Code);
        Assert.Equal(GuardSeverityValues.Blocked, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksDesktopSessionUnknownWhenInputDesktopNameCannotBeResolved()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            desktopSession: new DesktopSessionProbeResult(
                InputDesktopAvailable: true,
                ErrorCode: null,
                DesktopNameResolved: false,
                DesktopName: null));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.DesktopSession);

        Assert.Equal(GuardStatusValues.Unknown, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.InputDesktopIdentityUnknown, reason.Code);
        Assert.Equal(GuardSeverityValues.Warning, reason.Severity);
    }

    [Fact]
    public void BuildDomainsDoesNotMarkDesktopSessionReadyWhenInteractiveConfirmationIsMissing()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: false,
                ProcessSessionId: null,
                ActiveConsoleSessionId: 1,
                ConnectState: null,
                ClientProtocolType: null));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.DesktopSession);

        Assert.Equal(GuardStatusValues.Unknown, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.SessionQueryFailed, reason.Code);
        Assert.Equal(GuardSeverityValues.Warning, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksSessionAlignmentBlockedWhenProcessSessionDiffers()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 3,
                ActiveConsoleSessionId: 5,
                ConnectState: SessionConnectState.Active,
                ClientProtocolType: 0));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.SessionAlignment);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.ProcessNotInActiveConsoleSession, reason.Code);
        Assert.Equal(GuardSeverityValues.Blocked, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksSessionAlignmentReadyWhenProcessMatchesActiveConsole()
    {
        RuntimeGuardRawFacts facts = CreateFacts();

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.SessionAlignment);

        Assert.Equal(GuardStatusValues.Ready, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.SessionAlignedWithActiveConsole, reason.Code);
        Assert.Equal(GuardSeverityValues.Info, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksIntegrityBlockedBelowMedium()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            token: new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.Low,
                IntegrityRid: 0x1000,
                ElevationResolved: false,
                IsElevated: false,
                ElevationType: null,
                UiAccessResolved: true,
                UiAccess: false));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.Integrity);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.IntegrityBelowMedium, reason.Code);
    }

    [Fact]
    public void BuildDomainsMarksIntegrityDegradedForMediumLimitedToken()
    {
        RuntimeGuardRawFacts facts = CreateFacts();

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.Integrity);

        Assert.Equal(GuardStatusValues.Degraded, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget, reason.Code);
        Assert.Equal(GuardSeverityValues.Warning, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksIntegrityReadyForHighToken()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            token: new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.High,
                IntegrityRid: 0x3000,
                ElevationResolved: true,
                IsElevated: true,
                ElevationType: TokenElevationTypeValue.Full,
                UiAccessResolved: true,
                UiAccess: false));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.Integrity);

        Assert.Equal(GuardStatusValues.Ready, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.IntegrityReadyProfile, reason.Code);
        Assert.Equal(GuardSeverityValues.Info, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksUiAccessBlockedWhenFlagMissing()
    {
        RuntimeGuardRawFacts facts = CreateFacts();

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.UiAccess);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.UiAccessMissing, reason.Code);
        Assert.Equal(GuardSeverityValues.Blocked, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksUiAccessReadyWhenFlagPresent()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            token: new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.High,
                IntegrityRid: 0x3000,
                ElevationResolved: true,
                IsElevated: true,
                ElevationType: TokenElevationTypeValue.Full,
                UiAccessResolved: true,
                UiAccess: true));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.UiAccess);

        Assert.Equal(GuardStatusValues.Ready, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.UiAccessEnabled, reason.Code);
        Assert.Equal(GuardSeverityValues.Info, reason.Severity);
    }

    [Fact]
    public void BuildWarningsReturnsDomainAndCapabilityWarningsWithoutPlaceholders()
    {
        RuntimeGuardRawFacts facts = CreateFacts();
        ReadinessDomainStatus[] domains = RuntimeGuardPolicy.BuildDomains(facts);
        RuntimeReadinessSnapshot snapshot = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains: domains,
            Capabilities: RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), domains));

        GuardReason[] warnings = RuntimeGuardPolicy.BuildWarnings(snapshot);

        Assert.Equal(3, warnings.Length);
        Assert.Contains(warnings, item => item.Code == GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget);
        Assert.Contains(warnings, item => item.Code == GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified);
        Assert.Contains(warnings, item => item.Code == GuardReasonCodeValues.WaitShellVisualAvailable);
        Assert.DoesNotContain(warnings, item => item.Code == GuardReasonCodeValues.AssessmentNotImplemented);
        Assert.DoesNotContain(warnings, item => item.Code == GuardReasonCodeValues.CapabilitySessionTransition && item.Source == CapabilitySummaryValues.Input);
    }

    [Fact]
    public void BuildWarningsSkipsDeferredCapabilityWarningsWhenCapabilityIsBlocked()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 1,
                ActiveConsoleSessionId: 0xFFFFFFFF,
                ConnectState: SessionConnectState.Active,
                ClientProtocolType: 0));
        ReadinessDomainStatus[] domains = RuntimeGuardPolicy.BuildDomains(facts);
        RuntimeReadinessSnapshot snapshot = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains: domains,
            Capabilities: RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), domains));

        GuardReason[] warnings = RuntimeGuardPolicy.BuildWarnings(snapshot);

        Assert.DoesNotContain(warnings, item => item.Source == CapabilitySummaryValues.Input);
        Assert.DoesNotContain(warnings, item => item.Source == CapabilitySummaryValues.Clipboard);
        Assert.DoesNotContain(warnings, item => item.Source == CapabilitySummaryValues.Launch);
    }

    [Fact]
    public void BuildCapabilitiesDerivesHealthyBaselineWithoutPlaceholders()
    {
        CapabilityGuardSummary[] capabilities = RuntimeGuardPolicy.BuildCapabilities(
            CreateFacts(),
            CreateTopology(),
            RuntimeGuardPolicy.BuildDomains(CreateFacts()));

        Assert.Equal(
            [
                CapabilitySummaryValues.Capture,
                CapabilitySummaryValues.Uia,
                CapabilitySummaryValues.Wait,
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Clipboard,
                CapabilitySummaryValues.Launch,
            ],
            capabilities.Select(item => item.Capability).ToArray());
        Assert.Equal(GuardStatusValues.Ready, Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Capture).Status);
        Assert.Equal(GuardStatusValues.Degraded, Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Uia).Status);
        Assert.Equal(GuardStatusValues.Degraded, Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Wait).Status);
        Assert.Equal(GuardStatusValues.Blocked, Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Input).Status);
        Assert.Equal(GuardStatusValues.Blocked, Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Clipboard).Status);
        Assert.Equal(GuardStatusValues.Blocked, Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Launch).Status);
        Assert.Equal(
            GuardReasonCodeValues.CaptureReady,
            Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Capture).Reasons[0].Code);
        Assert.Equal(
            GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified,
            Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Uia).Reasons[0].Code);
        Assert.Equal(
            GuardReasonCodeValues.WaitShellVisualAvailable,
            Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Wait).Reasons[0].Code);
        Assert.Contains(
            Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Wait).Reasons,
            item => item.Code == GuardReasonCodeValues.WaitUiaBranchLaunchabilityUnverified);
        Assert.Equal(
            GuardReasonCodeValues.InputIntegrityLimited,
            Assert.Single(capabilities, item => item.Capability == CapabilitySummaryValues.Input).Reasons[1].Code);
        Assert.Equal(
            [
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Clipboard,
                CapabilitySummaryValues.Launch,
            ],
            RuntimeGuardPolicy.BuildBlockedCapabilities(capabilities).Select(item => item.Capability).ToArray());
    }

    [Fact]
    public void BuildCapabilitiesMarksCaptureDegradedWhenWindowsGraphicsCaptureIsUnavailable()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            capture: new CaptureCapabilityProbeResult(
                FactResolved: true,
                WindowsGraphicsCaptureSupported: false));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Capture);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Equal(GuardReasonCodeValues.CaptureDesktopFallbackOnly, capability.Reasons[0].Code);
    }

    [Fact]
    public void BuildCapabilitiesIncludesSessionTransitionAndStructuralCaptureReasonsTogether()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 1,
                ActiveConsoleSessionId: 0xFFFFFFFF,
                ConnectState: SessionConnectState.Active,
                ClientProtocolType: 0),
            capture: new CaptureCapabilityProbeResult(
                FactResolved: true,
                WindowsGraphicsCaptureSupported: false));
        DisplayTopologySnapshot topology = CreateTopology(includeMonitors: false);

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, topology, RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Capture);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.CapabilitySessionTransition);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.CaptureDesktopFallbackOnly);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.CaptureNoActiveMonitors);
    }

    [Fact]
    public void BuildCapabilitiesIncludesWgcAndMonitorReasonsWhenBothApply()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            capture: new CaptureCapabilityProbeResult(
                FactResolved: true,
                WindowsGraphicsCaptureSupported: false));
        DisplayTopologySnapshot topology = CreateTopology(includeMonitors: false);

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, topology, RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Capture);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.CaptureDesktopFallbackOnly);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.CaptureNoActiveMonitors);
    }

    [Fact]
    public void BuildCapabilitiesMarksCaptureDegradedWhenDisplayIdentityFallsBackToGdi()
    {
        RuntimeGuardRawFacts facts = CreateFacts();
        DisplayTopologySnapshot topology = CreateTopology(identityMode: DisplayIdentityModeValues.GdiFallback);

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, topology, RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Capture);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Equal(GuardReasonCodeValues.CaptureMonitorIdentityFallback, capability.Reasons[0].Code);
    }

    [Fact]
    public void BuildCapabilitiesMarksCaptureDegradedWhenNoActiveMonitorsExist()
    {
        RuntimeGuardRawFacts facts = CreateFacts();
        DisplayTopologySnapshot topology = CreateTopology(includeMonitors: false);

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, topology, RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Capture);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
    }

    [Fact]
    public void BuildCapabilitiesMarksUiaBlockedWhenWorkerBoundaryIsMissing()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            uia: new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: false,
                FailureReason: "UIA worker process не найден рядом с host output."));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Uia);

        Assert.Equal(GuardStatusValues.Blocked, capability.Status);
        Assert.Equal(GuardReasonCodeValues.UiaWorkerUnavailable, capability.Reasons[0].Code);
    }

    [Fact]
    public void BuildCapabilitiesPrefersWorkerMissingOverUnknownSessionForUia()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: false,
                ProcessSessionId: null,
                ActiveConsoleSessionId: 1,
                ConnectState: null,
                ClientProtocolType: null),
            uia: new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: false,
                FailureReason: "UIA worker process не найден рядом с host output."));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Uia);

        Assert.Equal(GuardStatusValues.Blocked, capability.Status);
        Assert.Equal(GuardReasonCodeValues.UiaWorkerUnavailable, capability.Reasons[0].Code);
    }

    [Fact]
    public void BuildCapabilitiesMarksWaitDegradedWhenOnlyShellAndVisualRemain()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            uia: new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: false,
                FailureReason: "UIA worker process не найден рядом с host output."));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Wait);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Equal(GuardReasonCodeValues.WaitShellVisualAvailable, capability.Reasons[0].Code);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.WaitUiaBranchUnavailable);
    }

    [Fact]
    public void BuildCapabilitiesMarksWaitDegradedWhenOnlyShellRemainsIfUiaIsOnlyConfigured()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            capture: new CaptureCapabilityProbeResult(
                FactResolved: false,
                WindowsGraphicsCaptureSupported: false));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Wait);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Equal(GuardReasonCodeValues.WaitShellOnlyAvailable, capability.Reasons[0].Code);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.WaitUiaBranchLaunchabilityUnverified);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.WaitVisualBranchUnknown);
    }

    [Fact]
    public void BuildCapabilitiesMarksWaitDegradedWhenOnlyActiveWindowMatchRemains()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            capture: new CaptureCapabilityProbeResult(
                FactResolved: true,
                WindowsGraphicsCaptureSupported: false),
            uia: new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: false,
                FailureReason: "UIA worker process не найден рядом с host output."));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Wait);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Equal(GuardReasonCodeValues.WaitShellOnlyAvailable, capability.Reasons[0].Code);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.WaitUiaBranchUnavailable);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.WaitVisualBranchUnavailable);
    }

    [Fact]
    public void BuildCapabilitiesKeepsWaitSubsetWhenUiaFactIsUnknown()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            uia: new UiaCapabilityProbeResult(
                FactResolved: false,
                WorkerLaunchSpecResolved: false,
                FailureReason: null));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Wait);

        Assert.Equal(GuardStatusValues.Degraded, capability.Status);
        Assert.Equal(GuardReasonCodeValues.WaitShellVisualAvailable, capability.Reasons[0].Code);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.WaitUiaBranchUnknown);
    }

    [Fact]
    public void BuildCapabilitiesPrefersSessionTransitionReasonForDeferredCapabilities()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            sessionAlignment: new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 1,
                ActiveConsoleSessionId: 0xFFFFFFFF,
                ConnectState: SessionConnectState.Active,
                ClientProtocolType: 0));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Input);

        Assert.Equal(GuardStatusValues.Blocked, capability.Status);
        Assert.Equal(GuardReasonCodeValues.CapabilitySessionTransition, capability.Reasons[1].Code);
    }

    [Fact]
    public void BuildCapabilitiesDoesNotEmitConcreteInputBlockerWhenUiAccessIsUnknown()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            token: new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.High,
                IntegrityRid: 0x3000,
                ElevationResolved: true,
                IsElevated: true,
                ElevationType: TokenElevationTypeValue.Full,
                UiAccessResolved: false,
                UiAccess: false));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Input);

        Assert.Equal(GuardStatusValues.Blocked, capability.Status);
        Assert.Equal(GuardReasonCodeValues.CapabilityPrerequisitesUnknown, capability.Reasons[1].Code);
        Assert.DoesNotContain(capability.Reasons, item => item.Code == GuardReasonCodeValues.InputUipiBarrierPresent);
    }

    [Fact]
    public void BuildCapabilitiesUsesIntegrityReasonBeforeUiAccessForMediumInputProfile()
    {
        RuntimeGuardRawFacts facts = CreateFacts();

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Input);

        Assert.Equal(GuardReasonCodeValues.InputIntegrityLimited, capability.Reasons[1].Code);
    }

    [Fact]
    public void BuildCapabilitiesAlwaysIncludesLaunchBoundaryWhenEnvironmentLooksReady()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            token: new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.High,
                IntegrityRid: 0x3000,
                ElevationResolved: true,
                IsElevated: true,
                ElevationType: TokenElevationTypeValue.Full,
                UiAccessResolved: true,
                UiAccess: true));

        CapabilityGuardSummary capability = Assert.Single(
            RuntimeGuardPolicy.BuildCapabilities(facts, CreateTopology(), RuntimeGuardPolicy.BuildDomains(facts)),
            item => item.Capability == CapabilitySummaryValues.Launch);

        Assert.Equal(GuardStatusValues.Blocked, capability.Status);
        Assert.Contains(capability.Reasons, item => item.Code == GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed);
    }

    private static RuntimeGuardRawFacts CreateFacts(
        DesktopSessionProbeResult? desktopSession = null,
        SessionAlignmentProbeResult? sessionAlignment = null,
        TokenProbeResult? token = null,
        CaptureCapabilityProbeResult? capture = null,
        UiaCapabilityProbeResult? uia = null) =>
        new(
            DesktopSession: desktopSession ?? new DesktopSessionProbeResult(
                InputDesktopAvailable: true,
                ErrorCode: null,
                DesktopNameResolved: true,
                DesktopName: "Default"),
            SessionAlignment: sessionAlignment ?? new SessionAlignmentProbeResult(
                ProcessSessionResolved: true,
                ProcessSessionId: 1,
                ActiveConsoleSessionId: 1,
                ConnectState: SessionConnectState.Active,
                ClientProtocolType: 0),
            Token: token ?? new TokenProbeResult(
                IntegrityResolved: true,
                IntegrityLevel: RuntimeIntegrityLevel.Medium,
                IntegrityRid: 0x2000,
                ElevationResolved: true,
                IsElevated: false,
                ElevationType: TokenElevationTypeValue.Limited,
                UiAccessResolved: true,
                UiAccess: false))
        {
            Capture = capture ?? new CaptureCapabilityProbeResult(
                FactResolved: true,
                WindowsGraphicsCaptureSupported: true),
            Uia = uia ?? new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: true,
                FailureReason: null),
        };

    private static DisplayTopologySnapshot CreateTopology(
        string identityMode = DisplayIdentityModeValues.DisplayConfigStrong,
        bool includeMonitors = true) =>
        new(
            Monitors: includeMonitors
                ? [
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
                ]
                : [],
            Diagnostics: new DisplayIdentityDiagnostics(
                IdentityMode: identityMode,
                FailedStage: identityMode == DisplayIdentityModeValues.GdiFallback
                    ? DisplayIdentityFailureStageValues.QueryDisplayConfig
                    : null,
                ErrorCode: identityMode == DisplayIdentityModeValues.GdiFallback ? 5 : null,
                ErrorName: identityMode == DisplayIdentityModeValues.GdiFallback ? "ERROR_ACCESS_DENIED" : null,
                MessageHuman: identityMode == DisplayIdentityModeValues.GdiFallback
                    ? "Display identity деградировала в `gdi:` fallback."
                    : "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
                CapturedAtUtc: DateTimeOffset.UtcNow));
}
