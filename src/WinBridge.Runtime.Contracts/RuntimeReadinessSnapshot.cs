namespace WinBridge.Runtime.Contracts;

public static class ReadinessDomainValues
{
    public const string DesktopSession = "desktop_session";
    public const string SessionAlignment = "session_alignment";
    public const string Integrity = "integrity";
    public const string UiAccess = "uiaccess";
}

public sealed record RuntimeReadinessSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ReadinessDomainStatus> Domains,
    IReadOnlyList<CapabilityGuardSummary> Capabilities);
