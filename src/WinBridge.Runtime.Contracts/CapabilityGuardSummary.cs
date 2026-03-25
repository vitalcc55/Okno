namespace WinBridge.Runtime.Contracts;

public static class CapabilitySummaryValues
{
    public const string Capture = "capture";
    public const string Uia = "uia";
    public const string Wait = "wait";
    public const string Input = "input";
    public const string Clipboard = "clipboard";
    public const string Launch = "launch";
}

public sealed record CapabilityGuardSummary(
    string Capability,
    string Status,
    IReadOnlyList<GuardReason> Reasons);
