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
}

public sealed record GuardReason(
    string Code,
    string Severity,
    string MessageHuman,
    string Source);
