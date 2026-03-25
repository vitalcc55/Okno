using System.Globalization;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Guards;

internal static class RuntimeGuardPolicy
{
    private const uint InvalidSessionId = 0xFFFFFFFF;
    private static readonly string[] ObserveCapabilities =
    [
        CapabilitySummaryValues.Capture,
        CapabilitySummaryValues.Uia,
        CapabilitySummaryValues.Wait,
    ];

    private static readonly string[] DeferredCapabilities =
    [
        CapabilitySummaryValues.Input,
        CapabilitySummaryValues.Clipboard,
        CapabilitySummaryValues.Launch,
    ];

    public static ReadinessDomainStatus[] BuildDomains(RuntimeGuardRawFacts facts) =>
    [
        BuildDesktopSession(facts),
        BuildSessionAlignment(facts.SessionAlignment),
        BuildIntegrity(facts.Token),
        BuildUiAccess(facts.Token),
    ];

    public static CapabilityGuardSummary[] BuildCapabilities() =>
    [
        .. ObserveCapabilities.Select(CreateUnknownCapability),
        .. DeferredCapabilities.Select(CreateBlockedCapability),
    ];

    public static CapabilityGuardSummary[] BuildBlockedCapabilities(IEnumerable<CapabilityGuardSummary> capabilities) =>
        capabilities
            .Where(capability => string.Equals(capability.Status, GuardStatusValues.Blocked, StringComparison.Ordinal))
            .ToArray();

    public static GuardReason[] BuildWarnings(RuntimeReadinessSnapshot snapshot) =>
        snapshot.Domains
            .SelectMany(domain => domain.Reasons)
            .Concat(snapshot.Capabilities.SelectMany(capability => capability.Reasons))
            .Where(reason => string.Equals(reason.Severity, GuardSeverityValues.Warning, StringComparison.Ordinal))
            .ToArray();

    private static ReadinessDomainStatus BuildDesktopSession(RuntimeGuardRawFacts facts)
    {
        if (!facts.DesktopSession.InputDesktopAvailable)
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.InputDesktopUnavailable,
                        GuardSeverityValues.Blocked,
                        ReadinessDomainValues.DesktopSession,
                        "Runtime не смог открыть input desktop; GUI action path нельзя считать доступным.")
                ]);
        }

        if (facts.SessionAlignment.ActiveConsoleSessionId == InvalidSessionId)
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.ActiveConsoleUnavailable,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.DesktopSession,
                        "Input desktop доступен, но active console session временно отсутствует; среда может быть в переходном состоянии.")
                ]);
        }

        if (facts.SessionAlignment.ProcessSessionResolved
            && facts.SessionAlignment.ProcessSessionId is uint processSessionId
            && processSessionId != facts.SessionAlignment.ActiveConsoleSessionId)
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.ProcessNotInActiveConsoleSession,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.DesktopSession,
                        "Input desktop доступен, но session текущего процесса не совпадает с active console session.")
                ]);
        }

        if (facts.SessionAlignment.ConnectState is SessionConnectState connectState
            && connectState != SessionConnectState.Active)
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.SessionNotInteractive,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.DesktopSession,
                        $"Input desktop открыт, но текущая session не находится в active interactive state ({ToContractValue(connectState)}).")
                ]);
        }

        if (facts.SessionAlignment.ConnectState is null
            && (!facts.SessionAlignment.ProcessSessionResolved || facts.SessionAlignment.ProcessSessionId is null))
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Unknown,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.SessionQueryFailed,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.DesktopSession,
                        "Runtime открыл input desktop, но не смог подтвердить interactive state текущей session.")
                ]);
        }

        return new(
            Domain: ReadinessDomainValues.DesktopSession,
            Status: GuardStatusValues.Ready,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.InputDesktopAvailable,
                    GuardSeverityValues.Info,
                    ReadinessDomainValues.DesktopSession,
                    "Runtime успешно открыл input desktop текущей interactive session.")
            ]);
    }

    private static ReadinessDomainStatus BuildSessionAlignment(SessionAlignmentProbeResult facts)
    {
        if (!facts.ProcessSessionResolved || facts.ProcessSessionId is null)
        {
            return new(
                Domain: ReadinessDomainValues.SessionAlignment,
                Status: GuardStatusValues.Unknown,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.SessionQueryFailed,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.SessionAlignment,
                        "Runtime не смог определить session id текущего процесса через ProcessIdToSessionId.")
                ]);
        }

        if (facts.ActiveConsoleSessionId == InvalidSessionId)
        {
            return new(
                Domain: ReadinessDomainValues.SessionAlignment,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.ActiveConsoleUnavailable,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.SessionAlignment,
                        "Windows временно не сообщает active console session; среда находится в attach/detach transition.")
                ]);
        }

        if (facts.ProcessSessionId.Value != facts.ActiveConsoleSessionId)
        {
            string detail = string.Format(
                CultureInfo.InvariantCulture,
                "Текущий процесс работает в session {0}, а active console сейчас {1}.",
                facts.ProcessSessionId.Value,
                facts.ActiveConsoleSessionId);
            return new(
                Domain: ReadinessDomainValues.SessionAlignment,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.ProcessNotInActiveConsoleSession,
                        GuardSeverityValues.Blocked,
                        ReadinessDomainValues.SessionAlignment,
                        detail)
                ]);
        }

        return new(
            Domain: ReadinessDomainValues.SessionAlignment,
            Status: GuardStatusValues.Ready,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.SessionAlignedWithActiveConsole,
                    GuardSeverityValues.Info,
                    ReadinessDomainValues.SessionAlignment,
                    "Session текущего процесса совпадает с active console session.")
            ]);
    }

    private static ReadinessDomainStatus BuildIntegrity(TokenProbeResult facts)
    {
        if (!facts.IntegrityResolved || facts.IntegrityLevel is null)
        {
            return new(
                Domain: ReadinessDomainValues.Integrity,
                Status: GuardStatusValues.Unknown,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.IntegrityQueryFailed,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.Integrity,
                        "Runtime не смог получить integrity profile текущего token через GetTokenInformation.")
                ]);
        }

        return facts.IntegrityLevel.Value switch
        {
            RuntimeIntegrityLevel.Untrusted or RuntimeIntegrityLevel.Low => new(
                Domain: ReadinessDomainValues.Integrity,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.IntegrityBelowMedium,
                        GuardSeverityValues.Blocked,
                        ReadinessDomainValues.Integrity,
                        "Integrity level текущего token ниже medium; стандартный desktop automation path нельзя считать доступным.")
                ]),
            RuntimeIntegrityLevel.Medium => new(
                Domain: ReadinessDomainValues.Integrity,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.IntegrityRequiresEqualOrLowerTarget,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.Integrity,
                        facts.ElevationType == TokenElevationTypeValue.Limited
                            ? "Текущий token работает как limited/medium; interaction с higher-integrity target нельзя обещать по умолчанию."
                            : "Текущий token имеет medium integrity; interaction с higher-integrity target нельзя обещать по умолчанию.")
                ]),
            RuntimeIntegrityLevel.High or RuntimeIntegrityLevel.SystemOrAbove => new(
                Domain: ReadinessDomainValues.Integrity,
                Status: GuardStatusValues.Ready,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.IntegrityReadyProfile,
                        GuardSeverityValues.Info,
                        ReadinessDomainValues.Integrity,
                        "Текущий token имеет high/system-or-above integrity profile; это не отменяет отдельные focus/UIA и target-specific ограничения.")
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(facts), facts.IntegrityLevel, null),
        };
    }

    private static ReadinessDomainStatus BuildUiAccess(TokenProbeResult facts)
    {
        if (!facts.UiAccessResolved)
        {
            return new(
                Domain: ReadinessDomainValues.UiAccess,
                Status: GuardStatusValues.Unknown,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.UiAccessQueryFailed,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.UiAccess,
                        "Runtime не смог определить наличие uiAccess flag в текущем token.")
                ]);
        }

        if (!facts.UiAccess)
        {
            return new(
                Domain: ReadinessDomainValues.UiAccess,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.UiAccessMissing,
                        GuardSeverityValues.Blocked,
                        ReadinessDomainValues.UiAccess,
                        "В текущем token отсутствует uiAccess; bypass обычного UIPI barrier нельзя считать доступным.")
                ]);
        }

        return new(
            Domain: ReadinessDomainValues.UiAccess,
            Status: GuardStatusValues.Ready,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.UiAccessEnabled,
                    GuardSeverityValues.Info,
                    ReadinessDomainValues.UiAccess,
                    "Текущий token содержит uiAccess, но этот flag не заменяет integrity-based target checks и не обещает любой cross-IL path.")
            ]);
    }

    private static CapabilityGuardSummary CreateUnknownCapability(string capability) =>
        new(
            Capability: capability,
            Status: GuardStatusValues.Unknown,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.AssessmentNotImplemented,
                    GuardSeverityValues.Warning,
                    capability,
                    "Probe-backed capability derivation для этого capability будет добавлена в Package C; статус остаётся консервативно unknown.")
            ]);

    private static CapabilityGuardSummary CreateBlockedCapability(string capability) =>
        new(
            Capability: capability,
            Status: GuardStatusValues.Blocked,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.CapabilityNotImplemented,
                    GuardSeverityValues.Blocked,
                    capability,
                    "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.")
            ]);

    private static GuardReason CreateReason(
        string code,
        string severity,
        string source,
        string messageHuman) =>
        new(
            Code: code,
            Severity: severity,
            MessageHuman: messageHuman,
            Source: source);

    private static string ToContractValue(SessionConnectState state) =>
        state switch
        {
            SessionConnectState.Active => "active",
            SessionConnectState.Connected => "connected",
            SessionConnectState.ConnectQuery => "connect_query",
            SessionConnectState.Shadow => "shadow",
            SessionConnectState.Disconnected => "disconnected",
            SessionConnectState.Idle => "idle",
            SessionConnectState.Listen => "listen",
            SessionConnectState.Reset => "reset",
            SessionConnectState.Down => "down",
            SessionConnectState.Init => "init",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
}
