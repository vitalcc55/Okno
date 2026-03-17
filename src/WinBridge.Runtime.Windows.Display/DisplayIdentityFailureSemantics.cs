using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

internal static class DisplayIdentityFailureSemantics
{
    public static bool IsIdentityBreaking(string? failedStage) =>
        failedStage is DisplayIdentityFailureStageValues.GetMonitorInfo
            or DisplayIdentityFailureStageValues.GetBufferSizes
            or DisplayIdentityFailureStageValues.QueryDisplayConfig
            or DisplayIdentityFailureStageValues.GetSourceName
            or DisplayIdentityFailureStageValues.CoverageGap;

    public static int GetPriority(string? failedStage) =>
        failedStage switch
        {
            DisplayIdentityFailureStageValues.GetBufferSizes => 500,
            DisplayIdentityFailureStageValues.QueryDisplayConfig => 400,
            DisplayIdentityFailureStageValues.GetMonitorInfo => 300,
            DisplayIdentityFailureStageValues.GetSourceName => 200,
            DisplayIdentityFailureStageValues.CoverageGap => 100,
            DisplayIdentityFailureStageValues.GetTargetName => 50,
            _ => 0,
        };
}
