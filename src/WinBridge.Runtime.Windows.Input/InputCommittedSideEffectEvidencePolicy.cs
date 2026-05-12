// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputCommittedSideEffectEvidencePolicy
{
    public static string Resolve(
        InputResult result,
        string? failureStage,
        InputCommittedSideEffectContext? committedContext)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (TryResolvePreciseDoubleClickEvidence(failureStage, committedContext, out string? preciseDoubleClickEvidence))
        {
            return preciseDoubleClickEvidence!;
        }

        if (string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialCompensated, StringComparison.Ordinal))
        {
            return "partial_dispatch_compensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialUncompensated, StringComparison.Ordinal))
        {
            return "partial_dispatch_uncompensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.ClickDispatchCleanFailure, StringComparison.Ordinal))
        {
            return committedContext?.Phase == InputIrreversiblePhase.AfterDoubleClickFirstTap
                ? "double_click_first_tap_committed_before_failure"
                : "cursor_move_committed_click_dispatch_clean_failure";
        }

        if (string.Equals(failureStage, InputFailureStageValues.TextDispatchCommittedFailure, StringComparison.Ordinal))
        {
            return "text_dispatch_committed_before_failure";
        }

        if (string.Equals(failureStage, InputFailureStageValues.KeypressDispatchPartialCompensated, StringComparison.Ordinal))
        {
            return "keyboard_dispatch_partial_compensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.KeypressDispatchPartialUncompensated, StringComparison.Ordinal))
        {
            return "keyboard_dispatch_partial_uncompensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.KeypressDispatchCommittedFailure, StringComparison.Ordinal))
        {
            return "keyboard_dispatch_committed_before_failure";
        }

        if (string.Equals(failureStage, InputFailureStageValues.DragDispatchNotStartedAfterMove, StringComparison.Ordinal))
        {
            return "cursor_move_committed_drag_dispatch_not_started";
        }

        if (string.Equals(failureStage, InputFailureStageValues.DragDispatchPartialCompensated, StringComparison.Ordinal))
        {
            return "drag_dispatch_partial_compensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.DragDispatchPartialUncompensated, StringComparison.Ordinal))
        {
            return "drag_dispatch_partial_uncompensated";
        }

        if (string.Equals(failureStage, InputFailureStageValues.DragDispatchCommittedFailure, StringComparison.Ordinal))
        {
            return "drag_dispatch_committed_before_failure";
        }

        if (string.Equals(result.Status, InputStatusValues.VerifyNeeded, StringComparison.Ordinal)
            && result.CompletedActionCount > 0)
        {
            return "completed_actions_committed";
        }

        if (result.CompletedActionCount > 0 && result.FailedActionIndex is null)
        {
            return "previous_actions_committed";
        }

        if (TryResolvePhaseSpecificEvidence(committedContext, out string? phaseSpecificEvidence))
        {
            return phaseSpecificEvidence!;
        }

        if (TryGetFailedAction(result, out InputActionResult? failedAction)
            && failedAction.ResolvedScreenPoint is not null)
        {
            return string.Equals(failureStage, InputFailureStageValues.CursorMove, StringComparison.Ordinal)
                ? "cursor_move_committed_before_failure"
                : "action_side_effect_committed_before_failure";
        }

        return result.CompletedActionCount > 0
            ? "completed_actions_committed"
            : "no_committed_side_effect_observed";
    }

    private static bool TryResolvePhaseSpecificEvidence(
        InputCommittedSideEffectContext? committedContext,
        out string? evidence)
    {
        evidence = committedContext?.Phase switch
        {
            InputIrreversiblePhase.AfterMove => "cursor_move_committed_before_failure",
            InputIrreversiblePhase.AfterClickTap => "click_dispatch_committed_before_failure",
            InputIrreversiblePhase.AfterDoubleClickFirstTap => "double_click_first_tap_committed_before_failure",
            InputIrreversiblePhase.AfterDoubleClickSecondTap => "double_click_both_taps_committed_before_failure",
            InputIrreversiblePhase.AfterTypeTextDispatch => "text_dispatch_committed_before_failure",
            InputIrreversiblePhase.AfterKeypressDispatch => "keyboard_dispatch_committed_before_failure",
            InputIrreversiblePhase.AfterScrollDispatch => "scroll_dispatch_committed_before_failure",
            InputIrreversiblePhase.AfterDragDispatch => "drag_dispatch_committed_before_failure",
            _ => null,
        };

        return evidence is not null;
    }

    private static bool TryResolvePreciseDoubleClickEvidence(
        string? failureStage,
        InputCommittedSideEffectContext? committedContext,
        out string? evidence)
    {
        evidence = committedContext?.Phase switch
        {
            InputIrreversiblePhase.AfterDoubleClickFirstTap when string.Equals(failureStage, InputFailureStageValues.ClickDispatchCleanFailure, StringComparison.Ordinal)
                => "double_click_first_tap_committed_before_failure",
            InputIrreversiblePhase.AfterDoubleClickFirstTap when string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialCompensated, StringComparison.Ordinal)
                => "double_click_first_tap_partial_compensated",
            InputIrreversiblePhase.AfterDoubleClickFirstTap when string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialUncompensated, StringComparison.Ordinal)
                => "double_click_first_tap_partial_uncompensated",
            InputIrreversiblePhase.AfterDoubleClickSecondTap when string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialCompensated, StringComparison.Ordinal)
                => "double_click_second_tap_partial_compensated",
            InputIrreversiblePhase.AfterDoubleClickSecondTap when string.Equals(failureStage, InputFailureStageValues.ClickDispatchPartialUncompensated, StringComparison.Ordinal)
                => "double_click_second_tap_partial_uncompensated",
            _ => null,
        };

        return evidence is not null;
    }

    private static bool TryGetFailedAction(InputResult result, out InputActionResult failedAction)
    {
        failedAction = null!;
        if (result.FailedActionIndex is not int failedIndex || result.Actions is null)
        {
            return false;
        }

        if (failedIndex < 0 || failedIndex >= result.Actions.Count)
        {
            return false;
        }

        failedAction = result.Actions[failedIndex];
        return true;
    }
}
