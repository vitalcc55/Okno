using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;

namespace WinBridge.Runtime.Tests;

public sealed class RuntimeGuardPolicyTests
{
    [Fact]
    public void BuildDomainsMarksDesktopSessionBlockedWhenInputDesktopUnavailable()
    {
        RuntimeGuardRawFacts facts = CreateFacts(
            desktopSession: new DesktopSessionProbeResult(InputDesktopAvailable: false, ErrorCode: 5));

        ReadinessDomainStatus domain = Assert.Single(
            RuntimeGuardPolicy.BuildDomains(facts),
            item => item.Domain == ReadinessDomainValues.DesktopSession);

        Assert.Equal(GuardStatusValues.Blocked, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.InputDesktopUnavailable, reason.Code);
        Assert.Equal(GuardSeverityValues.Blocked, reason.Severity);
    }

    [Fact]
    public void BuildDomainsMarksDesktopSessionDegradedWhenSessionDisconnected()
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

        Assert.Equal(GuardStatusValues.Degraded, domain.Status);
        GuardReason reason = Assert.Single(domain.Reasons);
        Assert.Equal(GuardReasonCodeValues.SessionNotInteractive, reason.Code);
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
    public void BuildWarningsReturnsOnlyWarningSeverityReasons()
    {
        RuntimeGuardRawFacts facts = CreateFacts();
        RuntimeReadinessSnapshot snapshot = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains: RuntimeGuardPolicy.BuildDomains(facts),
            Capabilities: RuntimeGuardPolicy.BuildCapabilities());

        GuardReason[] warnings = RuntimeGuardPolicy.BuildWarnings(snapshot);

        Assert.Equal(4, warnings.Length);
        Assert.Contains(warnings, item => item.Code == GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget);
        Assert.Equal(3, warnings.Count(item => item.Code == GuardReasonCodeValues.AssessmentNotImplemented));
    }

    [Fact]
    public void BuildCapabilitiesKeepsObserveUnknownAndDeferredBlocked()
    {
        CapabilityGuardSummary[] capabilities = RuntimeGuardPolicy.BuildCapabilities();

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
        Assert.All(
            capabilities.Where(item =>
                item.Capability is CapabilitySummaryValues.Capture
                    or CapabilitySummaryValues.Uia
                    or CapabilitySummaryValues.Wait),
            item => Assert.Equal(GuardStatusValues.Unknown, item.Status));
        Assert.All(
            capabilities.Where(item =>
                item.Capability is CapabilitySummaryValues.Input
                    or CapabilitySummaryValues.Clipboard
                    or CapabilitySummaryValues.Launch),
            item => Assert.Equal(GuardStatusValues.Blocked, item.Status));
        Assert.Equal(
            [
                CapabilitySummaryValues.Input,
                CapabilitySummaryValues.Clipboard,
                CapabilitySummaryValues.Launch,
            ],
            RuntimeGuardPolicy.BuildBlockedCapabilities(capabilities).Select(item => item.Capability).ToArray());
    }

    private static RuntimeGuardRawFacts CreateFacts(
        DesktopSessionProbeResult? desktopSession = null,
        SessionAlignmentProbeResult? sessionAlignment = null,
        TokenProbeResult? token = null) =>
        new(
            DesktopSession: desktopSession ?? new DesktopSessionProbeResult(InputDesktopAvailable: true, ErrorCode: null),
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
                UiAccess: false));
}
