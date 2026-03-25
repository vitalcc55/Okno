namespace WinBridge.Runtime.Contracts;

public static class GuardSeverityValues
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Blocked = "blocked";
}

public static class GuardReasonCodeValues
{
    public const string AssessmentNotImplemented = "assessment_not_implemented";
    public const string CapabilityNotImplemented = "capability_not_implemented";
    public const string InputDesktopUnavailable = "input_desktop_unavailable";
    public const string InputDesktopAvailable = "input_desktop_available";
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
}

public sealed record GuardReason(
    string Code,
    string Severity,
    string MessageHuman,
    string Source);
