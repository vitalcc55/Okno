namespace WinBridge.Runtime.Contracts;

public static class GuardSeverityValues
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Blocked = "blocked";
}

public static class GuardReasonCodeValues
{
    public const string CapabilityNotImplemented = "capability_not_implemented";
    public const string CapabilitySessionBlocked = "capability_session_blocked";
    public const string CapabilityPrerequisitesUnknown = "capability_prerequisites_unknown";
    public const string CapabilitySessionTransition = "capability_session_transition";
    public const string InputDesktopUnavailable = "input_desktop_unavailable";
    public const string InputDesktopAvailable = "input_desktop_available";
    public const string InputDesktopIdentityUnknown = "input_desktop_identity_unknown";
    public const string InputDesktopNonDefault = "input_desktop_non_default";
    public const string SessionNotInteractive = "session_not_interactive";
    public const string SessionQueryFailed = "session_query_failed";
    public const string ActiveConsoleUnavailable = "active_console_unavailable";
    public const string ProcessNotInActiveConsoleSession = "process_not_in_active_console_session";
    public const string SessionAlignedWithActiveConsole = "session_aligned_with_active_console";
    public const string IntegrityQueryFailed = "integrity_query_failed";
    public const string IntegrityBelowMedium = "integrity_below_medium";
    public const string IntegrityRequiresEqualOrLowerTarget = "integrity_requires_equal_or_lower_target";
    public const string IntegrityReadyProfile = "integrity_ready_profile";
    public const string UiAccessQueryFailed = "uiaccess_query_failed";
    public const string UiAccessMissing = "uiaccess_missing";
    public const string UiAccessEnabled = "uiaccess_enabled";
    public const string CaptureReady = "capture_ready";
    public const string CaptureNoActiveMonitors = "capture_no_active_monitors";
    public const string CaptureDesktopFallbackOnly = "capture_desktop_fallback_only";
    public const string CaptureMonitorIdentityFallback = "capture_monitor_identity_fallback";
    public const string UiaWorkerUnavailable = "uia_worker_unavailable";
    public const string UiaWorkerLaunchabilityUnverified = "uia_worker_launchability_unverified";
    public const string UiaObserveScopeLimited = "uia_observe_scope_limited";
    public const string WaitShellVisualAvailable = "wait_shell_visual_available";
    public const string WaitShellOnlyAvailable = "wait_shell_only_available";
    public const string WaitUiaBranchLaunchabilityUnverified = "wait_uia_branch_launchability_unverified";
    public const string WaitUiaBranchUnknown = "wait_uia_branch_unknown";
    public const string WaitVisualBranchUnknown = "wait_visual_branch_unknown";
    public const string WaitUiaBranchUnavailable = "wait_uia_branch_unavailable";
    public const string WaitVisualBranchUnavailable = "wait_visual_branch_unavailable";
    public const string WaitVisualConditionUnavailable = "wait_visual_condition_unavailable";
    public const string WaitUiaConditionsUnavailable = "wait_uia_conditions_unavailable";
    public const string InputUipiBarrierPresent = "input_uipi_barrier_present";
    public const string InputIntegrityLimited = "input_integrity_limited";
    public const string ClipboardIntegrityLimited = "clipboard_integrity_limited";
    public const string LaunchElevationBoundaryUnconfirmed = "launch_elevation_boundary_unconfirmed";
}

public sealed record GuardReason(
    string Code,
    string Severity,
    string MessageHuman,
    string Source);
