using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Guards;

public sealed class ToolExecutionGate(IRuntimeGuardService runtimeGuardService) : IToolExecutionGate
{
    private readonly IRuntimeGuardService _runtimeGuardService = runtimeGuardService;

    public ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent) =>
        Evaluate(policy, _runtimeGuardService.GetSnapshot(), intent);

    public ToolExecutionDecision Evaluate(
        ToolExecutionPolicyDescriptor policy,
        RuntimeGuardAssessment assessment,
        ToolExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(_runtimeGuardService);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy.GuardCapability);

        ToolExecutionDecision? capabilityResolution = TryResolveCapabilityDecision(policy, assessment, intent, out CapabilityGuardSummary? capability);
        if (capabilityResolution is not null)
        {
            return capabilityResolution;
        }

        CapabilityGuardSummary resolvedCapability = capability!;
        CapabilityStateKind capabilityState = ClassifyCapabilityState(resolvedCapability.Status);
        if (capabilityState == CapabilityStateKind.Invalid)
        {
            return CreateUnknownStatusDecision(policy, intent, resolvedCapability.Status);
        }

        ToolExecutionDecision? dryRunDecision = TryResolveDryRunDecision(policy, resolvedCapability, intent);
        if (dryRunDecision is not null)
        {
            return dryRunDecision;
        }

        IReadOnlyList<GuardReason> reasons = resolvedCapability.Reasons;
        if (capabilityState is CapabilityStateKind.Blocked or CapabilityStateKind.Unknown)
        {
            if (policy.SupportsDryRun && intent.PreviewAvailable && !intent.IsDryRunRequested)
            {
                return CreateDecision(
                    ToolExecutionDecisionKind.DryRunOnly,
                    ToolExecutionMode.DryRun,
                    policy,
                    reasons,
                    requiresConfirmation: false);
            }

            if (intent.IsDryRunRequested)
            {
                return CreateDecision(
                    ToolExecutionDecisionKind.Allowed,
                    ToolExecutionMode.DryRun,
                    policy,
                    reasons,
                    requiresConfirmation: false);
            }

            return CreateDecision(
                ToolExecutionDecisionKind.Blocked,
                GetRequestedMode(intent),
                policy,
                reasons,
                requiresConfirmation: false);
        }

        if (intent.IsDryRunRequested)
        {
            return CreateDecision(
                ToolExecutionDecisionKind.Allowed,
                ToolExecutionMode.DryRun,
                policy,
                reasons,
                requiresConfirmation: false);
        }

        bool confirmationRequired =
            (!intent.ConfirmationGranted && policy.ConfirmationMode == ToolExecutionConfirmationMode.Required)
            || (!intent.ConfirmationGranted
                && policy.ConfirmationMode == ToolExecutionConfirmationMode.Conditional
                && capabilityState == CapabilityStateKind.Degraded);
        if (confirmationRequired)
        {
            return CreateDecision(
                ToolExecutionDecisionKind.NeedsConfirmation,
                ToolExecutionMode.Live,
                policy,
                reasons,
                requiresConfirmation: true);
        }

        return CreateDecision(
            ToolExecutionDecisionKind.Allowed,
            ToolExecutionMode.Live,
            policy,
            reasons,
            requiresConfirmation: false);
    }

    private static ToolExecutionDecision? TryResolveCapabilityDecision(
        ToolExecutionPolicyDescriptor policy,
        RuntimeGuardAssessment assessment,
        ToolExecutionIntent intent,
        out CapabilityGuardSummary? capability)
    {
        CapabilityGuardSummary[] matches =
        [
            .. assessment.Readiness.Capabilities.Where(
                item => string.Equals(item.Capability, policy.GuardCapability, StringComparison.Ordinal)),
        ];

        capability = matches.Length == 1 ? matches[0] : null;
        return matches.Length switch
        {
            0 => CreateMissingCapabilityDecision(policy, intent),
            > 1 => CreateAmbiguousCapabilityDecision(policy, intent),
            _ => null,
        };
    }

    private static ToolExecutionDecision? TryResolveDryRunDecision(
        ToolExecutionPolicyDescriptor policy,
        CapabilityGuardSummary capability,
        ToolExecutionIntent intent)
    {
        if (!intent.IsDryRunRequested)
        {
            return null;
        }

        if (!policy.SupportsDryRun)
        {
            return CreateDecision(
                ToolExecutionDecisionKind.Blocked,
                ToolExecutionMode.DryRun,
                policy,
                [
                    new GuardReason(
                        GuardReasonCodeValues.CapabilityDryRunNotSupported,
                        GuardSeverityValues.Blocked,
                        $"Tool policy for `{policy.GuardCapability}` does not support dry-run execution.",
                        policy.GuardCapability),
                ],
                requiresConfirmation: false);
        }

        if (!intent.PreviewAvailable)
        {
            return CreateDecision(
                ToolExecutionDecisionKind.Blocked,
                ToolExecutionMode.DryRun,
                policy,
                [
                    new GuardReason(
                        GuardReasonCodeValues.CapabilityDryRunPreviewUnavailable,
                        GuardSeverityValues.Blocked,
                        $"Dry-run execution for `{policy.GuardCapability}` requires an explicit preview-capable path.",
                        policy.GuardCapability),
                ],
                requiresConfirmation: false);
        }

        return CreateDecision(
            ToolExecutionDecisionKind.Allowed,
            ToolExecutionMode.DryRun,
            policy,
            capability.Reasons,
            requiresConfirmation: false);
    }

    private static ToolExecutionDecision CreateMissingCapabilityDecision(
        ToolExecutionPolicyDescriptor policy,
        ToolExecutionIntent intent) =>
        CreateDecision(
            ToolExecutionDecisionKind.Blocked,
            GetRequestedMode(intent),
            policy,
            [
                new GuardReason(
                    GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                    GuardSeverityValues.Blocked,
                    $"Runtime readiness snapshot не содержит capability summary для `{policy.GuardCapability}`.",
                    policy.GuardCapability),
            ],
            requiresConfirmation: false);

    private static ToolExecutionDecision CreateAmbiguousCapabilityDecision(
        ToolExecutionPolicyDescriptor policy,
        ToolExecutionIntent intent) =>
        CreateDecision(
            ToolExecutionDecisionKind.Blocked,
            GetRequestedMode(intent),
            policy,
            [
                new GuardReason(
                    GuardReasonCodeValues.CapabilitySummaryAmbiguous,
                    GuardSeverityValues.Blocked,
                    $"Runtime readiness snapshot содержит больше одной capability summary для `{policy.GuardCapability}`.",
                    policy.GuardCapability),
            ],
            requiresConfirmation: false);

    private static ToolExecutionDecision CreateUnknownStatusDecision(
        ToolExecutionPolicyDescriptor policy,
        ToolExecutionIntent intent,
        string status) =>
        CreateDecision(
            ToolExecutionDecisionKind.Blocked,
            GetRequestedMode(intent),
            policy,
            [
                new GuardReason(
                    GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                    GuardSeverityValues.Blocked,
                    $"Runtime readiness snapshot вернул неподдерживаемый capability status `{status}` для `{policy.GuardCapability}`.",
                    policy.GuardCapability),
            ],
            requiresConfirmation: false);

    private static ToolExecutionDecision CreateDecision(
        ToolExecutionDecisionKind kind,
        ToolExecutionMode mode,
        ToolExecutionPolicyDescriptor policy,
        IReadOnlyList<GuardReason> reasons,
        bool requiresConfirmation) =>
        new(
            Kind: kind,
            Mode: mode,
            RiskLevel: policy.RiskLevel,
            Reasons: reasons,
            RequiresConfirmation: requiresConfirmation,
            DryRunSupported: policy.SupportsDryRun,
            GuardCapability: policy.GuardCapability);

    private static ToolExecutionMode GetRequestedMode(ToolExecutionIntent intent) =>
        intent.IsDryRunRequested ? ToolExecutionMode.DryRun : ToolExecutionMode.Live;

    private static CapabilityStateKind ClassifyCapabilityState(string status)
    {
        if (string.Equals(status, GuardStatusValues.Blocked, StringComparison.Ordinal))
        {
            return CapabilityStateKind.Blocked;
        }

        if (string.Equals(status, GuardStatusValues.Unknown, StringComparison.Ordinal))
        {
            return CapabilityStateKind.Unknown;
        }

        if (string.Equals(status, GuardStatusValues.Ready, StringComparison.Ordinal))
        {
            return CapabilityStateKind.Ready;
        }

        if (string.Equals(status, GuardStatusValues.Degraded, StringComparison.Ordinal))
        {
            return CapabilityStateKind.Degraded;
        }

        return CapabilityStateKind.Invalid;
    }

    private enum CapabilityStateKind
    {
        Ready,
        Degraded,
        Blocked,
        Unknown,
        Invalid,
    }
}
