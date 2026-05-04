// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Tests;

public sealed class DisplayIdentityDiagnosticsPipelineTests
{
    [Fact]
    public void IdentityBreakingSourceFailureWinsOverEarlierTargetNameDegradation()
    {
        DisplayIdentityFailureInfo? failure = null;

        failure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayIdentityFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.GetTargetName,
                ErrorCode: 50,
                ErrorName: "ERROR_NOT_SUPPORTED",
                MessageHuman: "Friendly name lookup degraded."));

        failure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayIdentityFailureInfo(
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

    [Fact]
    public void MonitorEnumerationFailureWinsOverEarlierTargetNameDegradation()
    {
        DisplayIdentityFailureInfo? failure = null;

        failure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayIdentityFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.GetTargetName,
                ErrorCode: 50,
                ErrorName: "ERROR_NOT_SUPPORTED",
                MessageHuman: "Friendly name lookup degraded."));

        failure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayIdentityFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.GetMonitorInfo,
                ErrorCode: 5,
                ErrorName: "ERROR_ACCESS_DENIED",
                MessageHuman: "Monitor enumeration failed."));

        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: failure?.FailedStage,
                ErrorCode: failure?.ErrorCode,
                ErrorName: failure?.ErrorName,
                MessageHuman: failure?.MessageHuman),
            usedFallbackMonitorIdentity: true,
            activeMonitorCount: 1,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.GdiFallback, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.GetMonitorInfo, diagnostics.FailedStage);
        Assert.Equal("ERROR_ACCESS_DENIED", diagnostics.ErrorName);
    }

    [Fact]
    public void QueryDisplayConfigFailureWinsOverLaterMonitorEnumerationFailure()
    {
        DisplayIdentityFailureInfo? failure = null;

        failure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayIdentityFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.QueryDisplayConfig,
                ErrorCode: 5,
                ErrorName: "ERROR_ACCESS_DENIED",
                MessageHuman: "QueryDisplayConfig failed."));

        failure = DisplayIdentityFailureAggregator.SelectMoreSignificant(
            failure,
            new DisplayIdentityFailureInfo(
                FailedStage: DisplayIdentityFailureStageValues.GetMonitorInfo,
                ErrorCode: 50,
                ErrorName: "ERROR_NOT_SUPPORTED",
                MessageHuman: "Monitor enumeration failed."));

        DisplayIdentityDiagnostics diagnostics = DisplayIdentityDiagnosticsBuilder.Build(
            new DisplayConfigQueryDiagnostics(
                FailedStage: failure?.FailedStage,
                ErrorCode: failure?.ErrorCode,
                ErrorName: failure?.ErrorName,
                MessageHuman: failure?.MessageHuman),
            usedFallbackMonitorIdentity: true,
            activeMonitorCount: 1,
            capturedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal(DisplayIdentityModeValues.GdiFallback, diagnostics.IdentityMode);
        Assert.Equal(DisplayIdentityFailureStageValues.QueryDisplayConfig, diagnostics.FailedStage);
        Assert.Equal("ERROR_ACCESS_DENIED", diagnostics.ErrorName);
    }
}
