// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputCommittedSideEffectEvidencePolicyTests
{
    [Theory]
    [InlineData(InputFailureStageValues.ClickDispatchPartialCompensated, "partial_dispatch_compensated")]
    [InlineData(InputFailureStageValues.ClickDispatchPartialUncompensated, "partial_dispatch_uncompensated")]
    [InlineData(InputFailureStageValues.DragDispatchPartialCompensated, "drag_dispatch_partial_compensated")]
    public void ResolveMapsKnownPartialFailureStages(string failureStage, string expectedEvidence)
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(CreateResult(), failureStage, committedContext: null);

        Assert.Equal(expectedEvidence, evidence);
    }

    [Fact]
    public void ResolveReturnsCompletedActionsCommittedForVerifyNeededBatch()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                status: InputStatusValues.VerifyNeeded,
                completedActionCount: 1),
            failureStage: null,
            committedContext: null);

        Assert.Equal("completed_actions_committed", evidence);
    }

    [Fact]
    public void ResolveReturnsPreviousActionsCommittedWhenCompletedActionsExistWithoutFailedIndex()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                status: InputStatusValues.Failed,
                completedActionCount: 1,
                failedActionIndex: null),
            failureStage: null,
            committedContext: null);

        Assert.Equal("previous_actions_committed", evidence);
    }

    [Fact]
    public void ResolveReturnsCursorMoveCommittedBeforeFailureForResolvedFailedAction()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Click,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260)),
                ]),
            InputFailureStageValues.CursorMove,
            committedContext: null);

        Assert.Equal("cursor_move_committed_before_failure", evidence);
    }

    [Fact]
    public void ResolveReturnsActionSideEffectCommittedBeforeFailureForResolvedFailedAction()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Click,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260)),
                ]),
            InputFailureStageValues.InputDispatch,
            committedContext: null);

        Assert.Equal("action_side_effect_committed_before_failure", evidence);
    }

    [Fact]
    public void ResolveReturnsDoubleClickFirstTapCommittedForCleanSecondTapFailure()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.DoubleClick,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            InputFailureStageValues.ClickDispatchCleanFailure,
            CreateCommittedContext(InputIrreversiblePhase.AfterDoubleClickFirstTap));

        Assert.Equal("double_click_first_tap_committed_before_failure", evidence);
    }

    [Fact]
    public void ResolveReturnsDoubleClickFirstTapCommittedForCancellationAfterFirstTap()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.DoubleClick,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            InputFailureStageValues.CancellationAfterCommittedSideEffect,
            CreateCommittedContext(InputIrreversiblePhase.AfterDoubleClickFirstTap));

        Assert.Equal("double_click_first_tap_committed_before_failure", evidence);
    }

    [Fact]
    public void ResolveReturnsDoubleClickBothTapsCommittedForCancellationAfterSecondTap()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.DoubleClick,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            InputFailureStageValues.CancellationAfterCommittedSideEffect,
            CreateCommittedContext(InputIrreversiblePhase.AfterDoubleClickSecondTap));

        Assert.Equal("double_click_both_taps_committed_before_failure", evidence);
    }

    [Fact]
    public void ResolveReturnsFirstTapPartialCompensatedEvidenceForDoubleClickPartialFailure()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.DoubleClick,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            InputFailureStageValues.ClickDispatchPartialCompensated,
            CreateCommittedContext(InputIrreversiblePhase.AfterDoubleClickFirstTap));

        Assert.Equal("double_click_first_tap_partial_compensated", evidence);
    }

    [Fact]
    public void ResolveReturnsSecondTapPartialUncompensatedEvidenceForDoubleClickPartialFailure()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.DoubleClick,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            InputFailureStageValues.ClickDispatchPartialUncompensated,
            CreateCommittedContext(InputIrreversiblePhase.AfterDoubleClickSecondTap));

        Assert.Equal("double_click_second_tap_partial_uncompensated", evidence);
    }

    [Fact]
    public void ResolvePreservesCursorMoveOnlyEvidenceForClickCleanFailureBeforeAnyTap()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Click,
                        Status: InputStatusValues.Failed,
                        ResolvedScreenPoint: new InputPoint(140, 260),
                        Button: InputButtonValues.Left),
                ]),
            InputFailureStageValues.ClickDispatchCleanFailure,
            CreateCommittedContext(InputIrreversiblePhase.AfterMove, actionType: InputActionTypeValues.Click));

        Assert.Equal("cursor_move_committed_click_dispatch_clean_failure", evidence);
    }

    [Fact]
    public void ResolveReturnsNoCommittedSideEffectObservedWhenNothingWasCommitted()
    {
        string evidence = InputCommittedSideEffectEvidencePolicy.Resolve(
            CreateResult(
                completedActionCount: 0,
                failedActionIndex: 0,
                actions:
                [
                    new InputActionResult(
                        Type: InputActionTypeValues.Click,
                        Status: InputStatusValues.Failed),
                ]),
            failureStage: null,
            committedContext: null);

        Assert.Equal("no_committed_side_effect_observed", evidence);
    }

    private static InputCommittedSideEffectContext CreateCommittedContext(
        InputIrreversiblePhase phase,
        string actionType = InputActionTypeValues.DoubleClick) =>
        new(
            ActionIndex: 0,
            Action: new InputAction
            {
                Type = actionType,
                CoordinateSpace = InputCoordinateSpaceValues.Screen,
                Point = new InputPoint(140, 260),
            },
            Phase: phase,
            ResolvedScreenPoint: new InputPoint(140, 260),
            Button: InputButtonValues.Left,
            TargetHwnd: 101);

    private static InputResult CreateResult(
        string status = InputStatusValues.Failed,
        int completedActionCount = 0,
        int? failedActionIndex = null,
        IReadOnlyList<InputActionResult>? actions = null) =>
        new(
            Status: status,
            Decision: status,
            CompletedActionCount: completedActionCount,
            FailedActionIndex: failedActionIndex,
            Actions: actions);
}
