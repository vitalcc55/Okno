namespace WinBridge.Runtime.Contracts;

public static class DisplayIdentityModeValues
{
    public const string DisplayConfigStrong = "display_config_strong";
    public const string GdiFallback = "gdi_fallback";
}

public static class DisplayIdentityFailureStageValues
{
    public const string CoverageGap = "display_config_coverage_gap";
    public const string GetMonitorInfo = "get_monitor_info";
    public const string GetBufferSizes = "get_buffer_sizes";
    public const string QueryDisplayConfig = "query_display_config";
    public const string GetSourceName = "get_source_name";
    public const string GetTargetName = "get_target_name";
}

public sealed record DisplayIdentityDiagnostics(
    string IdentityMode,
    string? FailedStage,
    int? ErrorCode,
    string? ErrorName,
    string MessageHuman,
    DateTimeOffset CapturedAtUtc);
