using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class ToolExecutionGateTests
{
    [Fact]
    public void EvaluateReturnsBlockedForHardBlockedCapability()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Input,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.InputUipiBarrierPresent,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Input,
                    "Future input path не может обещать higher-integrity interaction без uiAccess.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required),
            assessment,
            ToolExecutionIntent.Default);

        Assert.Equal(ToolExecutionDecisionKind.Blocked, decision.Kind);
        Assert.Equal(ToolExecutionMode.Live, decision.Mode);
        Assert.False(decision.RequiresConfirmation);
        Assert.False(decision.DryRunSupported);
        Assert.Equal(CapabilitySummaryValues.Input, decision.GuardCapability);
        Assert.Equal(GuardReasonCodeValues.InputUipiBarrierPresent, Assert.Single(decision.Reasons).Code);
    }

    [Fact]
    public void EvaluateReturnsNeedsConfirmationForReadyCapabilityWhenConfirmationIsRequired()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Clipboard,
                GuardStatusValues.Ready,
                CreateReason(
                    GuardReasonCodeValues.CaptureReady,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Clipboard,
                    "Clipboard path может быть выполнен.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Clipboard,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Clipboard,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            ToolExecutionIntent.Default);

        Assert.Equal(ToolExecutionDecisionKind.NeedsConfirmation, decision.Kind);
        Assert.Equal(ToolExecutionMode.Live, decision.Mode);
        Assert.True(decision.RequiresConfirmation);
        Assert.True(decision.DryRunSupported);
        Assert.Equal(GuardReasonCodeValues.CaptureReady, Assert.Single(decision.Reasons).Code);
    }

    [Fact]
    public void EvaluateReturnsDryRunOnlyForBlockedCapabilityWhenPreviewIsAvailable()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Clipboard,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.ClipboardIntegrityLimited,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Clipboard,
                    "Clipboard path пока не должен обещать операции при неполном integrity profile.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Clipboard,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Clipboard,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            new ToolExecutionIntent(
                IsDryRunRequested: false,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        Assert.Equal(ToolExecutionDecisionKind.DryRunOnly, decision.Kind);
        Assert.Equal(ToolExecutionMode.DryRun, decision.Mode);
        Assert.False(decision.RequiresConfirmation);
        Assert.True(decision.DryRunSupported);
        Assert.Equal(GuardReasonCodeValues.ClipboardIntegrityLimited, Assert.Single(decision.Reasons).Code);
    }

    [Fact]
    public void EvaluateReturnsAllowedLiveWhenConfirmationIsGranted()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Launch,
                GuardStatusValues.Ready,
                CreateReason(
                    GuardReasonCodeValues.LaunchReadyProfile,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Launch,
                    "Launch path может быть выполнен.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Launch,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Launch,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            new ToolExecutionIntent(
                IsDryRunRequested: false,
                ConfirmationGranted: true,
                PreviewAvailable: true));

        Assert.Equal(ToolExecutionDecisionKind.Allowed, decision.Kind);
        Assert.Equal(ToolExecutionMode.Live, decision.Mode);
        Assert.False(decision.RequiresConfirmation);
        Assert.True(decision.DryRunSupported);
    }

    [Fact]
    public void EvaluateReturnsNeedsConfirmationForDegradedLaunchCapabilityWhenConfirmationIsRequired()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Launch,
                GuardStatusValues.Degraded,
                CreateReason(
                    GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Launch,
                    "Live launch path остаётся confirmation-worthy: higher-integrity boundary заранее не подтверждена.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Launch,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Launch,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            ToolExecutionIntent.Default);

        Assert.Equal(ToolExecutionDecisionKind.NeedsConfirmation, decision.Kind);
        Assert.Equal(ToolExecutionMode.Live, decision.Mode);
        Assert.True(decision.RequiresConfirmation);
        Assert.True(decision.DryRunSupported);
        Assert.Equal(GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed, Assert.Single(decision.Reasons).Code);
    }

    [Fact]
    public void EvaluateReturnsAllowedDryRunWhenDryRunIsRequested()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Clipboard,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.ClipboardIntegrityLimited,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Clipboard,
                    "Clipboard path пока не должен обещать операции при неполном integrity profile.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Clipboard,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Clipboard,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            new ToolExecutionIntent(
                IsDryRunRequested: true,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        Assert.Equal(ToolExecutionDecisionKind.Allowed, decision.Kind);
        Assert.Equal(ToolExecutionMode.DryRun, decision.Mode);
        Assert.False(decision.RequiresConfirmation);
        Assert.True(decision.DryRunSupported);
    }

    [Fact]
    public void EvaluateReturnsBlockedWhenDryRunIsRequestedButPolicyDoesNotSupportIt()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Input,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.InputIntegrityLimited,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Input,
                    "Future input path ограничен текущим integrity profile.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required),
            assessment,
            new ToolExecutionIntent(
                IsDryRunRequested: true,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        GuardReason reason = Assert.Single(decision.Reasons);
        Assert.Equal(ToolExecutionDecisionKind.Blocked, decision.Kind);
        Assert.Equal(ToolExecutionMode.DryRun, decision.Mode);
        Assert.Equal(GuardReasonCodeValues.CapabilityDryRunNotSupported, reason.Code);
        Assert.Equal(CapabilitySummaryValues.Input, reason.Source);
    }

    [Fact]
    public void EvaluateReturnsBlockedWhenDryRunIsRequestedButPreviewIsUnavailable()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Clipboard,
                GuardStatusValues.Ready,
                CreateReason(
                    GuardReasonCodeValues.CaptureReady,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Clipboard,
                    "Clipboard path может быть выполнен.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Clipboard,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Clipboard,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            new ToolExecutionIntent(
                IsDryRunRequested: true,
                ConfirmationGranted: false,
                PreviewAvailable: false));

        GuardReason reason = Assert.Single(decision.Reasons);
        Assert.Equal(ToolExecutionDecisionKind.Blocked, decision.Kind);
        Assert.Equal(ToolExecutionMode.DryRun, decision.Mode);
        Assert.Equal(GuardReasonCodeValues.CapabilityDryRunPreviewUnavailable, reason.Code);
        Assert.Equal(CapabilitySummaryValues.Clipboard, reason.Source);
    }

    [Fact]
    public void EvaluateReturnsNeedsConfirmationForConditionalPolicyWhenCapabilityIsDegraded()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Uia,
                GuardStatusValues.Degraded,
                CreateReason(
                    GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Uia,
                    "Worker launch spec resolved, но runtime startability UIA boundary не подтверждена.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.UiaAction,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Uia,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Conditional),
            assessment,
            ToolExecutionIntent.Default);

        Assert.Equal(ToolExecutionDecisionKind.NeedsConfirmation, decision.Kind);
        Assert.Equal(ToolExecutionMode.Live, decision.Mode);
        Assert.True(decision.RequiresConfirmation);
        Assert.Equal(GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified, Assert.Single(decision.Reasons).Code);
    }

    [Fact]
    public void EvaluateFailsClosedWhenGuardCapabilitySummaryIsMissing()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Capture,
                GuardStatusValues.Ready,
                CreateReason(
                    GuardReasonCodeValues.CaptureReady,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Capture,
                    "Capture path готов.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required),
            assessment,
            ToolExecutionIntent.Default);

        GuardReason reason = Assert.Single(decision.Reasons);
        Assert.Equal(ToolExecutionDecisionKind.Blocked, decision.Kind);
        Assert.Equal(GuardReasonCodeValues.CapabilityPrerequisitesUnknown, reason.Code);
        Assert.Equal(CapabilitySummaryValues.Input, reason.Source);
    }

    [Fact]
    public void EvaluateFailsClosedForInvalidCapabilityStatusEvenWhenDryRunIsRequested()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Clipboard,
                status: "configured",
                CreateReason(
                    GuardReasonCodeValues.CaptureReady,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Clipboard,
                    "Clipboard path может быть выполнен.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Clipboard,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Clipboard,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.Required),
            assessment,
            new ToolExecutionIntent(
                IsDryRunRequested: true,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        GuardReason reason = Assert.Single(decision.Reasons);
        Assert.Equal(ToolExecutionDecisionKind.Blocked, decision.Kind);
        Assert.Equal(ToolExecutionMode.DryRun, decision.Mode);
        Assert.Equal(GuardReasonCodeValues.CapabilityPrerequisitesUnknown, reason.Code);
        Assert.Equal(CapabilitySummaryValues.Clipboard, reason.Source);
    }

    [Fact]
    public void EvaluateFailsClosedWhenGuardCapabilitySummaryIsAmbiguous()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Input,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.InputIntegrityLimited,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Input,
                    "Future input path ограничен текущим integrity profile.")),
            CreateCapability(
                CapabilitySummaryValues.Input,
                GuardStatusValues.Blocked,
                CreateReason(
                    GuardReasonCodeValues.InputUipiBarrierPresent,
                    GuardSeverityValues.Blocked,
                    CapabilitySummaryValues.Input,
                    "Future input path не может обещать higher-integrity interaction без uiAccess.")));
        ToolExecutionGate gate = CreateGate();

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Input,
                ToolExecutionRiskLevel.Destructive,
                CapabilitySummaryValues.Input,
                supportsDryRun: false,
                ToolExecutionConfirmationMode.Required),
            assessment,
            ToolExecutionIntent.Default);

        GuardReason reason = Assert.Single(decision.Reasons);
        Assert.Equal(ToolExecutionDecisionKind.Blocked, decision.Kind);
        Assert.Equal(GuardReasonCodeValues.CapabilitySummaryAmbiguous, reason.Code);
        Assert.Equal(CapabilitySummaryValues.Input, reason.Source);
    }

    [Fact]
    public void EvaluateUsesInjectedRuntimeGuardServiceSnapshot()
    {
        RuntimeGuardAssessment assessment = CreateAssessment(
            CreateCapability(
                CapabilitySummaryValues.Launch,
                GuardStatusValues.Ready,
                CreateReason(
                    GuardReasonCodeValues.IntegrityReadyProfile,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Launch,
                    "Launch path может быть выполнен.")));
        ToolExecutionGate gate = new(new StubRuntimeGuardService(assessment));

        ToolExecutionDecision decision = gate.Evaluate(
            CreatePolicy(
                ToolExecutionPolicyGroup.Launch,
                ToolExecutionRiskLevel.High,
                CapabilitySummaryValues.Launch,
                supportsDryRun: true,
                ToolExecutionConfirmationMode.None),
            new ToolExecutionIntent(
                IsDryRunRequested: false,
                ConfirmationGranted: false,
                PreviewAvailable: true));

        Assert.Equal(ToolExecutionDecisionKind.Allowed, decision.Kind);
        Assert.Equal(ToolExecutionMode.Live, decision.Mode);
    }

    private static ToolExecutionGate CreateGate() => new(new StubRuntimeGuardService(CreateAssessment()));

    private static ToolExecutionPolicyDescriptor CreatePolicy(
        ToolExecutionPolicyGroup policyGroup,
        ToolExecutionRiskLevel riskLevel,
        string guardCapability,
        bool supportsDryRun,
        ToolExecutionConfirmationMode confirmationMode) =>
        new(
            PolicyGroup: policyGroup,
            RiskLevel: riskLevel,
            GuardCapability: guardCapability,
            SupportsDryRun: supportsDryRun,
            ConfirmationMode: confirmationMode,
            RedactionClass: ToolExecutionRedactionClass.None);

    private static RuntimeGuardAssessment CreateAssessment(params CapabilityGuardSummary[] capabilities)
    {
        RuntimeReadinessSnapshot readiness = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains: [],
            Capabilities: capabilities);

        return new RuntimeGuardAssessment(
            Topology: CreateTopology(),
            Readiness: readiness,
            BlockedCapabilities:
            [
                .. capabilities.Where(item => item.Status == GuardStatusValues.Blocked),
            ],
            Warnings:
            [
                .. capabilities
                    .Where(item => item.Status != GuardStatusValues.Blocked)
                    .SelectMany(item => item.Reasons)
                    .Where(item => item.Severity == GuardSeverityValues.Warning),
            ]);
    }

    private static CapabilityGuardSummary CreateCapability(
        string capability,
        string status,
        params GuardReason[] reasons) =>
        new(
            Capability: capability,
            Status: status,
            Reasons: reasons);

    private static GuardReason CreateReason(
        string code,
        string severity,
        string source,
        string message) =>
        new(
            Code: code,
            Severity: severity,
            MessageHuman: message,
            Source: source);

    private static DisplayTopologySnapshot CreateTopology() =>
        new(
            Monitors: [],
            Diagnostics: new DisplayIdentityDiagnostics(
                IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
                FailedStage: null,
                ErrorCode: null,
                ErrorName: null,
                MessageHuman: "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
                CapturedAtUtc: DateTimeOffset.UtcNow));

    private sealed class StubRuntimeGuardService(RuntimeGuardAssessment assessment) : IRuntimeGuardService
    {
        public RuntimeGuardAssessment GetSnapshot() => assessment;
    }
}
