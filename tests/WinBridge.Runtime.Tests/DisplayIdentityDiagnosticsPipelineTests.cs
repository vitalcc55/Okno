using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class DisplayIdentityDiagnosticsPipelineTests
{
    [Fact]
    public void IdentityBreakingSourceFailureWinsOverEarlierTargetNameDegradation()
    {
        DisplayConfigFailureInfo? failure = null;

        failure = DisplayConfigFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayConfigFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.GetTargetName,
                ErrorCode: 50,
                ErrorName: "ERROR_NOT_SUPPORTED",
                MessageHuman: "Friendly name lookup degraded."));

        failure = DisplayConfigFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayConfigFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.GetSourceName,
                ErrorCode: 5,
                ErrorName: "ERROR_ACCESS_DENIED",
                MessageHuman: "Source identity lookup failed."));

        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: failure?.FailedStage,
                ErrorCode: failure?.ErrorCode,
                ErrorName: failure?.ErrorName,
                MessageHuman: failure?.MessageHuman),
            usedFallbackMonitorIdentity: false,
            activeMonitorCount: 1,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.GdiFallback, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.GetSourceName, diagnostics.FailedStage);
        Assert.Equal("ERROR_ACCESS_DENIED", diagnostics.ErrorName);
    }
}
