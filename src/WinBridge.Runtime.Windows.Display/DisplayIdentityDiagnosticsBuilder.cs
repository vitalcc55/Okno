using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Display;

internal sealed record DisplayConfigQueryDiagnostics(
    string? FailedStage,
    int? ErrorCode,
    string? ErrorName,
    string? MessageHuman);

internal static class DisplayIdentityDiagnosticsBuilder
{
    public static DisplayIdentityDiagnostics Build(
        DisplayConfigQueryDiagnostics queryDiagnostics,
        bool usedFallbackMonitorIdentity,
        int activeMonitorCount,
        DateTimeOffset capturedAtUtc)
    {
        bool identityFailure = DisplayIdentityFailureSemantics.IsIdentityBreaking(queryDiagnostics.FailedStage);
        if (identityFailure)
        {
            return new(
                IdentityMode: DisplayIdentityModeValues.GdiFallback,
                FailedStage: queryDiagnostics.FailedStage,
                ErrorCode: queryDiagnostics.ErrorCode,
                ErrorName: queryDiagnostics.ErrorName,
                MessageHuman: queryDiagnostics.MessageHuman ?? "Display identity деградировала в `gdi:` fallback.",
                CapturedAtUtc: capturedAtUtc);
        }

        if (usedFallbackMonitorIdentity)
        {
            string message = BuildCoverageFallbackMessage(queryDiagnostics, activeMonitorCount);
            return new(
                IdentityMode: DisplayIdentityModeValues.GdiFallback,
                FailedStage: DisplayIdentityFailureStageValues.CoverageGap,
                ErrorCode: null,
                ErrorName: null,
                MessageHuman: message,
                CapturedAtUtc: capturedAtUtc);
        }

        if (string.Equals(queryDiagnostics.FailedStage, DisplayIdentityFailureStageValues.GetTargetName, StringComparison.Ordinal))
        {
            return new(
                IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
                FailedStage: queryDiagnostics.FailedStage,
                ErrorCode: queryDiagnostics.ErrorCode,
                ErrorName: queryDiagnostics.ErrorName,
                MessageHuman: queryDiagnostics.MessageHuman ?? "Strong monitor identity resolved, but target friendly name lookup partially degraded.",
                CapturedAtUtc: capturedAtUtc);
        }

        return new(
            IdentityMode: DisplayIdentityModeValues.DisplayConfigStrong,
            FailedStage: null,
            ErrorCode: null,
            ErrorName: null,
            MessageHuman: "Strong monitor identity resolved through QueryDisplayConfig for all active desktop monitors.",
            CapturedAtUtc: capturedAtUtc);
    }

    private static string BuildCoverageFallbackMessage(
        DisplayConfigQueryDiagnostics queryDiagnostics,
        int activeMonitorCount)
    {
        string baseMessage = activeMonitorCount == 0
            ? "Display identity деградировала в `gdi:` fallback до появления активных monitor targets."
            : "Display identity деградировала в `gdi:` fallback для monitor targets, которые не были покрыты strong display identity.";

        if (string.Equals(queryDiagnostics.FailedStage, DisplayIdentityFailureStageValues.GetTargetName, StringComparison.Ordinal))
        {
            string detail = queryDiagnostics.ErrorName is null
                ? "Дополнительно деградировал lookup friendly name display target."
                : $"Дополнительно деградировал lookup friendly name display target ({queryDiagnostics.ErrorName}).";
            return baseMessage + " " + detail;
        }

        return baseMessage;
    }
}
