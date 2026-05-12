// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputFailureStagePolicyTests
{
    [Theory]
    [InlineData(InputFailureCodeValues.InvalidRequest, InputFailureStageValues.RequestValidation)]
    [InlineData(InputFailureCodeValues.MissingTarget, InputFailureStageValues.TargetResolution)]
    [InlineData(InputFailureCodeValues.TargetNotForeground, InputFailureStageValues.TargetPreflight)]
    [InlineData(InputFailureCodeValues.CaptureReferenceStale, InputFailureStageValues.CoordinateMapping)]
    [InlineData(InputFailureCodeValues.CursorMoveFailed, InputFailureStageValues.CursorMove)]
    [InlineData(InputFailureCodeValues.InputDispatchFailed, InputFailureStageValues.InputDispatch)]
    public void ResolveFailureStageMapsKnownFailureCodes(string failureCode, string expectedStage)
    {
        string? resolvedStage = InputFailureStagePolicy.ResolveFailureStage(failureCode);

        Assert.Equal(expectedStage, resolvedStage);
    }

    [Fact]
    public void MapClickDispatchFailureStageReturnsNullForSuccessfulDispatch()
    {
        string? stage = InputFailureStagePolicy.MapClickDispatchFailureStage(InputClickDispatchOutcomeKind.Success);

        Assert.Null(stage);
    }

    [Fact]
    public void MapClickDispatchFailureStageReturnsNullWhenDispatchWasNotAttempted()
    {
        string? stage = InputFailureStagePolicy.MapClickDispatchFailureStage(InputClickDispatchOutcomeKind.NotAttempted);

        Assert.Null(stage);
    }

    [Theory]
    [InlineData((int)InputClickDispatchOutcomeKind.CleanFailure, InputFailureStageValues.ClickDispatchCleanFailure)]
    [InlineData((int)InputClickDispatchOutcomeKind.PartialDispatchCompensated, InputFailureStageValues.ClickDispatchPartialCompensated)]
    [InlineData((int)InputClickDispatchOutcomeKind.PartialDispatchUncompensated, InputFailureStageValues.ClickDispatchPartialUncompensated)]
    public void MapClickDispatchFailureStageMapsPartialAndCleanFailures(
        int outcomeKindValue,
        string expectedStage)
    {
        string? stage = InputFailureStagePolicy.MapClickDispatchFailureStage((InputClickDispatchOutcomeKind)outcomeKindValue);

        Assert.Equal(expectedStage, stage);
    }

    [Fact]
    public void ResolveDragFailureStagePrefersExplicitHint()
    {
        string stage = InputFailureStagePolicy.ResolveDragFailureStage(
            new InputDispatchResult(
                Success: false,
                CommittedSideEffects: true,
                FailureStageHint: InputFailureStageValues.DragDispatchPartialCompensated));

        Assert.Equal(InputFailureStageValues.DragDispatchPartialCompensated, stage);
    }

    [Fact]
    public void ResolveDragFailureStageFallsBackToUncompensatedAfterCommittedEffects()
    {
        string stage = InputFailureStagePolicy.ResolveDragFailureStage(
            new InputDispatchResult(
                Success: false,
                CommittedSideEffects: true));

        Assert.Equal(InputFailureStageValues.DragDispatchPartialUncompensated, stage);
    }

    [Fact]
    public void ResolveDragFailureStageFallsBackToNotStartedAfterMoveWhenNoCommittedEffects()
    {
        string stage = InputFailureStagePolicy.ResolveDragFailureStage(
            new InputDispatchResult(
                Success: false,
                CommittedSideEffects: false));

        Assert.Equal(InputFailureStageValues.DragDispatchNotStartedAfterMove, stage);
    }
}
