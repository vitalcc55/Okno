using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class DisplayIdentityDiagnosticsBuilderTests
{
    [Fact]
    public void BuildReturnsGdiFallbackWhenQueryFailedBeforeAnyFallbackMonitorWasObserved()
    {
        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: DisplayIdentityFailureStageValues.QueryDisplayConfig,
                ErrorCode: 5,
                ErrorName: "ERROR_ACCESS_DENIED",
                MessageHuman: "QueryDisplayConfig failed."),
            usedFallbackMonitorIdentity: false,
            activeMonitorCount: 0,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.GdiFallback, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.QueryDisplayConfig, diagnostics.FailedStage);
        Assert.Equal("ERROR_ACCESS_DENIED", diagnostics.ErrorName);
    }

    [Fact]
    public void BuildPreservesStrongIdentityWhenOnlyFriendlyNameLookupDegrades()
    {
        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: DisplayIdentityFailureStageValues.GetTargetName,
                ErrorCode: 50,
                ErrorName: "ERROR_NOT_SUPPORTED",
                MessageHuman: "Friendly name lookup degraded."),
            usedFallbackMonitorIdentity: false,
            activeMonitorCount: 2,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.DisplayConfigStrong, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.GetTargetName, diagnostics.FailedStage);
        Assert.Equal("ERROR_NOT_SUPPORTED", diagnostics.ErrorName);
    }

    [Fact]
    public void BuildDoesNotReuseStrongIdentityMessageWhenCoverageAlreadyFellBackToGdi()
    {
        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: DisplayIdentityFailureStageValues.GetTargetName,
                ErrorCode: 50,
                ErrorName: "ERROR_NOT_SUPPORTED",
                MessageHuman: "Strong monitor identity preserved, but target friendly name lookup degraded."),
            usedFallbackMonitorIdentity: true,
            activeMonitorCount: 2,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.GdiFallback, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.CoverageGap, diagnostics.FailedStage);
        Assert.Contains("gdi", diagnostics.MessageHuman, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("strong monitor identity preserved", diagnostics.MessageHuman, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildEmitsTypedCoverageGapWhenDisplayConfigDidNotCoverAllMonitors()
    {
        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: null,
                ErrorCode: null,
                ErrorName: null,
                MessageHuman: null),
            usedFallbackMonitorIdentity: true,
            activeMonitorCount: 2,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.GdiFallback, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.CoverageGap, diagnostics.FailedStage);
        Assert.Null(diagnostics.ErrorCode);
        Assert.Null(diagnostics.ErrorName);
    }
}
