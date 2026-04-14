using System.Globalization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Guards;

internal static class RuntimeGuardPolicy
{
    private const uint InvalidSessionId = 0xFFFFFFFF;

    public static ReadinessDomainStatus[] BuildDomains(RuntimeGuardRawFacts facts) =>
    [
        BuildDesktopSession(facts),
        BuildSessionAlignment(facts.SessionAlignment),
        BuildIntegrity(facts.Token),
        BuildUiAccess(facts.Token),
    ];

    public static CapabilityGuardSummary[] BuildCapabilities(
        RuntimeGuardRawFacts facts,
        DisplayTopologySnapshot topology,
        IReadOnlyList<ReadinessDomainStatus> domains)
    {
        ReadinessDomainStatus desktopSession = GetDomain(domains, ReadinessDomainValues.DesktopSession);
        ReadinessDomainStatus sessionAlignment = GetDomain(domains, ReadinessDomainValues.SessionAlignment);
        ReadinessDomainStatus integrity = GetDomain(domains, ReadinessDomainValues.Integrity);
        ReadinessDomainStatus uiAccess = GetDomain(domains, ReadinessDomainValues.UiAccess);

        return
        [
            BuildCapture(facts, topology, desktopSession, sessionAlignment),
            BuildUia(facts, desktopSession, sessionAlignment),
            BuildWait(facts, desktopSession, sessionAlignment),
            BuildInput(desktopSession, sessionAlignment, integrity, uiAccess),
            BuildClipboard(desktopSession, sessionAlignment, integrity),
            BuildLaunch(desktopSession, sessionAlignment, integrity),
        ];
    }

    public static CapabilityGuardSummary[] BuildBlockedCapabilities(IEnumerable<CapabilityGuardSummary> capabilities) =>
        capabilities
            .Where(capability => string.Equals(capability.Status, GuardStatusValues.Blocked, StringComparison.Ordinal))
            .ToArray();

    public static GuardReason[] BuildWarnings(RuntimeReadinessSnapshot snapshot) =>
        snapshot.Domains
            .SelectMany(domain => domain.Reasons)
            .Concat(
                snapshot.Capabilities
                    .Where(capability => !string.Equals(capability.Status, GuardStatusValues.Blocked, StringComparison.Ordinal))
                    .SelectMany(capability => capability.Reasons))
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

        if (!facts.DesktopSession.DesktopNameResolved || string.IsNullOrWhiteSpace(facts.DesktopSession.DesktopName))
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Unknown,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.InputDesktopIdentityUnknown,
                        GuardSeverityValues.Warning,
                        ReadinessDomainValues.DesktopSession,
                        "Runtime открыл input desktop, но не смог определить его имя; usable automation surface нельзя подтвердить.")
                ]);
        }

        if (!string.Equals(facts.DesktopSession.DesktopName, "Default", StringComparison.Ordinal))
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.InputDesktopNonDefault,
                        GuardSeverityValues.Blocked,
                        ReadinessDomainValues.DesktopSession,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Текущий input desktop = '{0}', а не 'Default'; live GUI automation path нельзя считать доступным.",
                            facts.DesktopSession.DesktopName))
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

        GuardReason? nonInteractiveReason = CreateNonInteractiveSessionReason(facts.SessionAlignment);
        if (nonInteractiveReason is not null)
        {
            return new(
                Domain: ReadinessDomainValues.DesktopSession,
                Status: GuardStatusValues.Blocked,
                Reasons: [nonInteractiveReason]);
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
                    "Runtime успешно открыл input desktop 'Default' текущей interactive session.")
            ]);
    }

    private static GuardReason? CreateNonInteractiveSessionReason(SessionAlignmentProbeResult facts)
    {
        if (facts.ConnectState is not SessionConnectState connectState || connectState == SessionConnectState.Active)
        {
            return null;
        }

        return CreateReason(
            GuardReasonCodeValues.SessionNotInteractive,
            GuardSeverityValues.Blocked,
            ReadinessDomainValues.DesktopSession,
            $"Input desktop открыт, но текущая session не находится в active interactive state ({ToContractValue(connectState)}).");
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

    private static CapabilityGuardSummary BuildCapture(
        RuntimeGuardRawFacts facts,
        DisplayTopologySnapshot topology,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment)
    {
        GuardReason? sessionBlocker = CreateSessionBlockedReason(
            CapabilitySummaryValues.Capture,
            desktopSession,
            sessionAlignment,
            "Capture observe path нельзя обещать без usable interactive desktop/session.");
        if (sessionBlocker is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Capture,
                Status: GuardStatusValues.Blocked,
                Reasons: [sessionBlocker]);
        }

        GuardReason? unknownReason = CreatePrerequisitesUnknownReason(
            CapabilitySummaryValues.Capture,
            desktopSession,
            sessionAlignment,
            facts.Capture.FactResolved,
            "Runtime не смог подтвердить prerequisites для capture readiness.");
        if (unknownReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Capture,
                Status: GuardStatusValues.Unknown,
                Reasons: [unknownReason]);
        }

        List<GuardReason> degradedReasons = [];
        if (IsDegraded(desktopSession) || IsDegraded(sessionAlignment))
        {
            degradedReasons.Add(
                CreateReason(
                    GuardReasonCodeValues.CapabilitySessionTransition,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Capture,
                    BuildTransitionMessage(
                        "Capture observe path видит transition-state среды и остаётся только conservative degraded.",
                        desktopSession,
                        sessionAlignment)));
        }

        if (!facts.Capture.WindowsGraphicsCaptureSupported)
        {
            degradedReasons.Add(
                CreateReason(
                    GuardReasonCodeValues.CaptureDesktopFallbackOnly,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Capture,
                    "Windows Graphics Capture недоступен; window capture path и visual wait path не считаются готовыми, а desktop path остаётся только fallback."));
        }

        if (topology.Monitors.Count == 0)
        {
            degradedReasons.Add(
                CreateReason(
                    GuardReasonCodeValues.CaptureNoActiveMonitors,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Capture,
                    "В текущем topology snapshot нет активных monitor targets; desktop capture scope нельзя считать fully ready."));
        }

        if (string.Equals(topology.Diagnostics.IdentityMode, DisplayIdentityModeValues.GdiFallback, StringComparison.Ordinal))
        {
            degradedReasons.Add(
                CreateReason(
                    GuardReasonCodeValues.CaptureMonitorIdentityFallback,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Capture,
                    "Capture path остаётся рабочим, но monitor identity деградировала в gdi fallback; explicit desktop targeting следует считать менее надёжным."));
        }

        if (degradedReasons.Count > 0)
        {
            return new(
                Capability: CapabilitySummaryValues.Capture,
                Status: GuardStatusValues.Degraded,
                Reasons: degradedReasons);
        }

        return new(
            Capability: CapabilitySummaryValues.Capture,
            Status: GuardStatusValues.Ready,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.CaptureReady,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Capture,
                    "Runtime может честно обещать current shipped capture semantics: strong display identity и Windows Graphics Capture доступны.")
            ]);
    }

    private static CapabilityGuardSummary BuildUia(
        RuntimeGuardRawFacts facts,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment)
    {
        UiaBoundaryState uiaBoundaryState = GetUiaBoundaryState(facts.Uia);

        GuardReason? sessionBlocker = CreateSessionBlockedReason(
            CapabilitySummaryValues.Uia,
            desktopSession,
            sessionAlignment,
            "UIA observe path нельзя обещать без usable interactive desktop/session.");
        if (sessionBlocker is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Uia,
                Status: GuardStatusValues.Blocked,
                Reasons: [sessionBlocker]);
        }

        if (uiaBoundaryState == UiaBoundaryState.Unavailable)
        {
            return new(
                Capability: CapabilitySummaryValues.Uia,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.UiaWorkerUnavailable,
                        GuardSeverityValues.Blocked,
                        CapabilitySummaryValues.Uia,
                        facts.Uia.FailureReason ?? "UIA worker boundary недоступна, поэтому observe path нельзя считать готовым.")
                ]);
        }

        GuardReason? unknownReason = CreatePrerequisitesUnknownReason(
            CapabilitySummaryValues.Uia,
            desktopSession,
            sessionAlignment,
            uiaBoundaryState != UiaBoundaryState.Unknown,
            "Runtime не смог подтвердить prerequisites для UIA observe path.");
        if (unknownReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Uia,
                Status: GuardStatusValues.Unknown,
                Reasons: [unknownReason]);
        }

        List<GuardReason> degradedReasons = [];
        if (IsDegraded(desktopSession) || IsDegraded(sessionAlignment))
        {
            degradedReasons.Add(
                CreateReason(
                    GuardReasonCodeValues.CapabilitySessionTransition,
                    GuardSeverityValues.Warning,
                    CapabilitySummaryValues.Uia,
                    BuildTransitionMessage(
                        "UIA worker boundary доступна, но текущая session остаётся transitional и observe path нельзя считать fully stable.",
                        desktopSession,
                        sessionAlignment)));
        }

        degradedReasons.Add(
            CreateReason(
                GuardReasonCodeValues.UiaWorkerLaunchabilityUnverified,
                GuardSeverityValues.Warning,
                CapabilitySummaryValues.Uia,
                "Worker launch spec resolved, но runtime startability UIA boundary не подтверждена в reporting-first health path."));
        degradedReasons.Add(
            CreateReason(
                GuardReasonCodeValues.UiaObserveScopeLimited,
                GuardSeverityValues.Info,
                CapabilitySummaryValues.Uia,
                "Current UIA semantics ограничены window-scoped ElementFromHandle/control-view path и не обещают cross-user Run as reachability."));

        return new(
            Capability: CapabilitySummaryValues.Uia,
            Status: GuardStatusValues.Degraded,
            Reasons: degradedReasons);
    }

    private static CapabilityGuardSummary BuildWait(
        RuntimeGuardRawFacts facts,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment)
    {
        UiaBoundaryState uiaBoundaryState = GetUiaBoundaryState(facts.Uia);

        GuardReason? sessionBlocker = CreateSessionBlockedReason(
            CapabilitySummaryValues.Wait,
            desktopSession,
            sessionAlignment,
            "windows.wait нельзя обещать без usable interactive desktop/session.");
        if (sessionBlocker is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Wait,
                Status: GuardStatusValues.Blocked,
                Reasons: [sessionBlocker]);
        }

        GuardReason? domainUnknownReason = CreatePrerequisitesUnknownReason(
            CapabilitySummaryValues.Wait,
            desktopSession,
            sessionAlignment,
            true,
            "Runtime не смог подтвердить prerequisite facts для composed wait path.");
        if (domainUnknownReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Wait,
                Status: GuardStatusValues.Unknown,
                Reasons: [domainUnknownReason]);
        }

        bool sessionTransition = IsDegraded(desktopSession) || IsDegraded(sessionAlignment);
        WaitBranchState visualState = facts.Capture.FactResolved
            ? (facts.Capture.WindowsGraphicsCaptureSupported ? WaitBranchState.Available : WaitBranchState.Unavailable)
            : WaitBranchState.Unknown;

        List<GuardReason> reasons = [];
        string summaryCode;
        string summaryMessage;

        if (visualState == WaitBranchState.Available)
        {
            summaryCode = GuardReasonCodeValues.WaitShellVisualAvailable;
            summaryMessage = "windows.wait может честно обещать active_window_matches и visual_changed.";
        }
        else
        {
            summaryCode = GuardReasonCodeValues.WaitShellOnlyAvailable;
            summaryMessage = "windows.wait сейчас можно честно обещать только для active_window_matches.";
        }

        reasons.Add(
            CreateReason(
                summaryCode,
                GuardSeverityValues.Warning,
                CapabilitySummaryValues.Wait,
                AppendSessionTransitionDetail(summaryMessage, sessionTransition, desktopSession, sessionAlignment)));

        AddUiaWaitBranchReason(
            reasons,
            CapabilitySummaryValues.Wait,
            uiaBoundaryState,
            unverifiedCode: GuardReasonCodeValues.WaitUiaBranchLaunchabilityUnverified,
            unverifiedMessage: "UIA worker boundary только configured: launch spec resolved, но startability не подтверждена, поэтому UIA-based wait conditions не advertised как usable subset.",
            unknownCode: GuardReasonCodeValues.WaitUiaBranchUnknown,
            unknownMessage: "UIA prerequisites для wait пока не подтверждены.",
            unavailableCode: GuardReasonCodeValues.WaitUiaBranchUnavailable,
            unavailableMessage: "UIA-based conditions сейчас нельзя обещать.");
        AddWaitBranchReason(
            reasons,
            CapabilitySummaryValues.Wait,
            visualState,
            unknownCode: GuardReasonCodeValues.WaitVisualBranchUnknown,
            unknownMessage: "Visual prerequisites для wait пока не подтверждены.",
            unavailableCode: GuardReasonCodeValues.WaitVisualBranchUnavailable,
            unavailableMessage: "Condition visual_changed сейчас нельзя обещать.");

        return new(
            Capability: CapabilitySummaryValues.Wait,
            Status: GuardStatusValues.Degraded,
            Reasons: reasons);
    }

    private static CapabilityGuardSummary BuildInput(
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment,
        ReadinessDomainStatus integrity,
        ReadinessDomainStatus uiAccess)
    {
        GuardReason? environmentReason = CreateSessionEnvironmentReason(
            CapabilitySummaryValues.Input,
            desktopSession,
            sessionAlignment,
            "Input path требует usable interactive desktop/session без hidden recovery.");
        if (environmentReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Input,
                Status: environmentReason.Code == GuardReasonCodeValues.CapabilityPrerequisitesUnknown
                    ? GuardStatusValues.Unknown
                    : GuardStatusValues.Blocked,
                Reasons: [environmentReason]);
        }

        GuardReason? integrityUnknownReason = CreateCapabilityPrerequisiteUnknownReason(
            CapabilitySummaryValues.Input,
            integrity,
            "Input path не смог подтвердить integrity prerequisites.");
        if (integrityUnknownReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Input,
                Status: GuardStatusValues.Unknown,
                Reasons: [integrityUnknownReason]);
        }

        if (IsBlocked(integrity))
        {
            return new(
                Capability: CapabilitySummaryValues.Input,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.InputIntegrityLimited,
                        GuardSeverityValues.Blocked,
                        CapabilitySummaryValues.Input,
                        "Общий input baseline требует как минимум medium integrity profile для coordinate input path. " + FirstReasonMessage(integrity))
                ]);
        }

        if (IsDegraded(integrity))
        {
            GuardReason? uiAccessUnknownReason = CreateCapabilityPrerequisiteUnknownReason(
                CapabilitySummaryValues.Input,
                uiAccess,
                "Input path не смог подтвердить uiAccess prerequisites для medium-integrity profile.");
            if (uiAccessUnknownReason is not null)
            {
                return new(
                    Capability: CapabilitySummaryValues.Input,
                    Status: GuardStatusValues.Unknown,
                    Reasons: [uiAccessUnknownReason]);
            }

            if (IsReady(uiAccess))
            {
                return new(
                    Capability: CapabilitySummaryValues.Input,
                    Status: GuardStatusValues.Ready,
                    Reasons:
                    [
                        CreateReason(
                            GuardReasonCodeValues.InputReadyProfile,
                            GuardSeverityValues.Info,
                            CapabilitySummaryValues.Input,
                            "Общий input baseline подтверждён: interactive desktop/session стабильны, medium integrity дополнен uiAccess, а target-specific focus/UIPI/protected-UI checks остаются на runtime boundary.")
                    ]);
            }

            return new(
                Capability: CapabilitySummaryValues.Input,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.InputUipiBarrierPresent,
                        GuardSeverityValues.Warning,
                        CapabilitySummaryValues.Input,
                        "Общий input baseline допускает только equal-or-lower target path: medium integrity без uiAccess не подтверждает safe interaction с higher-integrity или protected UI targets.")
                ]);
        }

        return new(
            Capability: CapabilitySummaryValues.Input,
            Status: GuardStatusValues.Ready,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.InputReadyProfile,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Input,
                    "Общий input baseline подтверждён: interactive desktop/session стабильны, а текущий integrity profile достаточен для coordinate input path без отдельного shared safety follow-up.")
            ]);
    }

    private static CapabilityGuardSummary BuildClipboard(
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment,
        ReadinessDomainStatus integrity)
    {
        GuardReason? environmentBlocker = CreateSessionEnvironmentReason(
            CapabilitySummaryValues.Clipboard,
            desktopSession,
            sessionAlignment,
            "Future clipboard path требует usable interactive desktop/session.");
        environmentBlocker ??= CreateCapabilityPrerequisiteUnknownReason(
            CapabilitySummaryValues.Clipboard,
            integrity,
            "Clipboard path не смог подтвердить integrity prerequisites.");
        environmentBlocker ??= CreateCapabilityConstraintReason(
            CapabilitySummaryValues.Clipboard,
            integrity,
            GuardReasonCodeValues.ClipboardIntegrityLimited,
            "Clipboard path пока не должен обещать операции при неполном integrity profile.");

        return CreateDeferredBlockedCapability(CapabilitySummaryValues.Clipboard, environmentBlocker);
    }

    private static CapabilityGuardSummary BuildLaunch(
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment,
        ReadinessDomainStatus integrity)
    {
        GuardReason? environmentReason = CreateSessionEnvironmentReason(
            CapabilitySummaryValues.Launch,
            desktopSession,
            sessionAlignment,
            "Future launch path нельзя обещать без usable interactive desktop/session.");
        if (environmentReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Launch,
                Status: IsUnknown(sessionAlignment) || IsUnknown(desktopSession)
                    ? GuardStatusValues.Unknown
                    : GuardStatusValues.Blocked,
                Reasons: [environmentReason]);
        }

        GuardReason? integrityUnknownReason = CreateCapabilityPrerequisiteUnknownReason(
            CapabilitySummaryValues.Launch,
            integrity,
            "Future launch path не смог подтвердить integrity prerequisites.");
        if (integrityUnknownReason is not null)
        {
            return new(
                Capability: CapabilitySummaryValues.Launch,
                Status: GuardStatusValues.Unknown,
                Reasons: [integrityUnknownReason]);
        }

        if (IsBlocked(integrity))
        {
            return new(
                Capability: CapabilitySummaryValues.Launch,
                Status: GuardStatusValues.Blocked,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.LaunchIntegrityLimited,
                        GuardSeverityValues.Blocked,
                        CapabilitySummaryValues.Launch,
                        "Future launch path требует как минимум medium integrity profile. " + FirstReasonMessage(integrity))
                ]);
        }

        if (IsDegraded(integrity))
        {
            return new(
                Capability: CapabilitySummaryValues.Launch,
                Status: GuardStatusValues.Degraded,
                Reasons:
                [
                    CreateReason(
                        GuardReasonCodeValues.LaunchElevationBoundaryUnconfirmed,
                        GuardSeverityValues.Warning,
                        CapabilitySummaryValues.Launch,
                        "Live launch path остаётся confirmation-worthy: higher-integrity/elevation boundary заранее не подтверждена без target-specific manifest facts. " + FirstReasonMessage(integrity))
                ]);
        }

        return new(
            Capability: CapabilitySummaryValues.Launch,
            Status: GuardStatusValues.Ready,
            Reasons:
            [
                CreateReason(
                    GuardReasonCodeValues.LaunchReadyProfile,
                    GuardSeverityValues.Info,
                    CapabilitySummaryValues.Launch,
                    "Shared launch-readiness baseline подтверждён: interactive desktop/session стабильны, а текущий token имеет достаточный integrity profile для live launch path без отдельного safety follow-up внутри handler-а.")
            ]);
    }

    private static CapabilityGuardSummary CreateDeferredBlockedCapability(string capability, IEnumerable<GuardReason> extraReasons)
    {
        List<GuardReason> reasons =
        [
            CreateReason(
                GuardReasonCodeValues.CapabilityNotImplemented,
                GuardSeverityValues.Blocked,
                capability,
                "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.")
        ];

        reasons.AddRange(extraReasons);
        return new(
            Capability: capability,
            Status: GuardStatusValues.Blocked,
            Reasons: reasons);
    }

    private static CapabilityGuardSummary CreateDeferredBlockedCapability(string capability, GuardReason? extraReason)
    {
        List<GuardReason> extraReasons = [];
        if (extraReason is not null)
        {
            extraReasons.Add(extraReason);
        }

        return CreateDeferredBlockedCapability(capability, extraReasons);
    }

    private static GuardReason? CreateSessionBlockedReason(
        string capability,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment,
        string prefix)
    {
        if (IsBlocked(sessionAlignment))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilitySessionBlocked,
                GuardSeverityValues.Blocked,
                capability,
                prefix + " " + FirstReasonMessage(sessionAlignment));
        }

        if (IsBlocked(desktopSession))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilitySessionBlocked,
                GuardSeverityValues.Blocked,
                capability,
                prefix + " " + FirstReasonMessage(desktopSession));
        }

        return null;
    }

    private static GuardReason? CreateSessionEnvironmentReason(
        string capability,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment,
        string prefix)
    {
        if (IsBlocked(sessionAlignment))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilitySessionBlocked,
                GuardSeverityValues.Blocked,
                capability,
                prefix + " " + FirstReasonMessage(sessionAlignment));
        }

        if (IsBlocked(desktopSession))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilitySessionBlocked,
                GuardSeverityValues.Blocked,
                capability,
                prefix + " " + FirstReasonMessage(desktopSession));
        }

        if (IsDegraded(sessionAlignment))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilitySessionTransition,
                GuardSeverityValues.Warning,
                capability,
                prefix + " " + FirstReasonMessage(sessionAlignment));
        }

        if (IsDegraded(desktopSession))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilitySessionTransition,
                GuardSeverityValues.Warning,
                capability,
                prefix + " " + FirstReasonMessage(desktopSession));
        }

        if (IsUnknown(sessionAlignment))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                GuardSeverityValues.Warning,
                capability,
                prefix + " " + FirstReasonMessage(sessionAlignment));
        }

        if (IsUnknown(desktopSession))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                GuardSeverityValues.Warning,
                capability,
                prefix + " " + FirstReasonMessage(desktopSession));
        }

        return null;
    }

    private static GuardReason? CreatePrerequisitesUnknownReason(
        string capability,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment,
        bool runtimeFactsResolved,
        string prefix)
    {
        if (IsUnknown(sessionAlignment))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                GuardSeverityValues.Warning,
                capability,
                prefix + " " + FirstReasonMessage(sessionAlignment));
        }

        if (IsUnknown(desktopSession))
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                GuardSeverityValues.Warning,
                capability,
                prefix + " " + FirstReasonMessage(desktopSession));
        }

        if (!runtimeFactsResolved)
        {
            return CreateReason(
                GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
                GuardSeverityValues.Warning,
                capability,
                prefix);
        }

        return null;
    }

    private static GuardReason? CreateCapabilityPrerequisiteUnknownReason(
        string capability,
        ReadinessDomainStatus domain,
        string prefix)
    {
        if (!IsUnknown(domain))
        {
            return null;
        }

        return CreateReason(
            GuardReasonCodeValues.CapabilityPrerequisitesUnknown,
            GuardSeverityValues.Warning,
            capability,
            prefix + " " + FirstReasonMessage(domain));
    }

    private static GuardReason? CreateCapabilityConstraintReason(
        string capability,
        ReadinessDomainStatus domain,
        string code,
        string prefix)
    {
        if (!IsBlocked(domain) && !IsDegraded(domain))
        {
            return null;
        }

        return CreateReason(
            code,
            GuardSeverityValues.Blocked,
            capability,
            prefix + " " + FirstReasonMessage(domain));
    }

    private static void AddUiaWaitBranchReason(
        List<GuardReason> reasons,
        string capability,
        UiaBoundaryState branchState,
        string unverifiedCode,
        string unverifiedMessage,
        string unknownCode,
        string unknownMessage,
        string unavailableCode,
        string unavailableMessage)
    {
        if (branchState == UiaBoundaryState.Unknown)
        {
            reasons.Add(
                CreateReason(
                    unknownCode,
                    GuardSeverityValues.Warning,
                    capability,
                    unknownMessage));
            return;
        }

        if (branchState == UiaBoundaryState.Unavailable)
        {
            reasons.Add(
                CreateReason(
                    unavailableCode,
                    GuardSeverityValues.Warning,
                    capability,
                    unavailableMessage));
            return;
        }

        reasons.Add(
            CreateReason(
                unverifiedCode,
                GuardSeverityValues.Info,
                capability,
                unverifiedMessage));
    }

    private static void AddWaitBranchReason(
        List<GuardReason> reasons,
        string capability,
        WaitBranchState branchState,
        string unknownCode,
        string unknownMessage,
        string unavailableCode,
        string unavailableMessage)
    {
        if (branchState == WaitBranchState.Unknown)
        {
            reasons.Add(
                CreateReason(
                    unknownCode,
                    GuardSeverityValues.Warning,
                    capability,
                    unknownMessage));
            return;
        }

        if (branchState == WaitBranchState.Unavailable)
        {
            reasons.Add(
                CreateReason(
                    unavailableCode,
                    GuardSeverityValues.Warning,
                    capability,
                    unavailableMessage));
        }
    }

    private static string AppendSessionTransitionDetail(
        string message,
        bool sessionTransition,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment)
    {
        if (!sessionTransition)
        {
            return message;
        }

        return message + " " + BuildTransitionMessage(
            "Текущая session уже reported как degraded.",
            desktopSession,
            sessionAlignment);
    }

    private static string BuildTransitionMessage(
        string prefix,
        ReadinessDomainStatus desktopSession,
        ReadinessDomainStatus sessionAlignment)
    {
        if (IsDegraded(sessionAlignment))
        {
            return prefix + " " + FirstReasonMessage(sessionAlignment);
        }

        if (IsDegraded(desktopSession))
        {
            return prefix + " " + FirstReasonMessage(desktopSession);
        }

        return prefix;
    }

    private static string FirstReasonMessage(ReadinessDomainStatus domain) =>
        domain.Reasons.Count == 0
            ? "Runtime не предоставил detail по этому domain."
            : domain.Reasons[0].MessageHuman;

    private static ReadinessDomainStatus GetDomain(IReadOnlyList<ReadinessDomainStatus> domains, string domain) =>
        domains.First(item => string.Equals(item.Domain, domain, StringComparison.Ordinal));

    private static bool IsBlocked(ReadinessDomainStatus domain) =>
        string.Equals(domain.Status, GuardStatusValues.Blocked, StringComparison.Ordinal);

    private static bool IsUnknown(ReadinessDomainStatus domain) =>
        string.Equals(domain.Status, GuardStatusValues.Unknown, StringComparison.Ordinal);

    private static bool IsDegraded(ReadinessDomainStatus domain) =>
        string.Equals(domain.Status, GuardStatusValues.Degraded, StringComparison.Ordinal);

    private static bool IsReady(ReadinessDomainStatus domain) =>
        string.Equals(domain.Status, GuardStatusValues.Ready, StringComparison.Ordinal);

    private static UiaBoundaryState GetUiaBoundaryState(UiaCapabilityProbeResult facts)
    {
        if (!facts.FactResolved)
        {
            return UiaBoundaryState.Unknown;
        }

        return facts.WorkerLaunchSpecResolved
            ? UiaBoundaryState.ConfiguredUnverified
            : UiaBoundaryState.Unavailable;
    }

    private enum WaitBranchState
    {
        Available,
        Unavailable,
        Unknown,
    }

    private enum UiaBoundaryState
    {
        ConfiguredUnverified,
        Unavailable,
        Unknown,
    }

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
