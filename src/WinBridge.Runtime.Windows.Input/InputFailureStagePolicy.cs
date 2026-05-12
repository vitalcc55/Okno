// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputFailureStagePolicy
{
    public static string? ResolveFailureStage(string? failureCode) =>
        failureCode switch
        {
            InputFailureCodeValues.InvalidRequest
                or InputFailureCodeValues.UnsupportedActionType
                or InputFailureCodeValues.UnsupportedCoordinateSpace
                or InputFailureCodeValues.UnsupportedKey => InputFailureStageValues.RequestValidation,
            InputFailureCodeValues.MissingTarget
                or InputFailureCodeValues.StaleExplicitTarget
                or InputFailureCodeValues.StaleAttachedTarget => InputFailureStageValues.TargetResolution,
            InputFailureCodeValues.TargetNotForeground
                or InputFailureCodeValues.TargetMinimized
                or InputFailureCodeValues.TargetIntegrityBlocked => InputFailureStageValues.TargetPreflight,
            InputFailureCodeValues.CaptureReferenceRequired
                or InputFailureCodeValues.CaptureReferenceStale
                or InputFailureCodeValues.PointOutOfBounds => InputFailureStageValues.CoordinateMapping,
            InputFailureCodeValues.CursorMoveFailed => InputFailureStageValues.CursorMove,
            InputFailureCodeValues.UnsupportedKeyboardLayout => InputFailureStageValues.InputDispatch,
            InputFailureCodeValues.InputDispatchFailed => InputFailureStageValues.InputDispatch,
            _ => null,
        };

    public static string? MapClickDispatchFailureStage(InputClickDispatchOutcomeKind outcomeKind) =>
        outcomeKind switch
        {
            InputClickDispatchOutcomeKind.Success => null,
            InputClickDispatchOutcomeKind.NotAttempted => null,
            InputClickDispatchOutcomeKind.CleanFailure => InputFailureStageValues.ClickDispatchCleanFailure,
            InputClickDispatchOutcomeKind.PartialDispatchCompensated => InputFailureStageValues.ClickDispatchPartialCompensated,
            InputClickDispatchOutcomeKind.PartialDispatchUncompensated => InputFailureStageValues.ClickDispatchPartialUncompensated,
            _ => null,
        };

    public static string ResolveDragFailureStage(InputDispatchResult dragDispatchResult)
    {
        ArgumentNullException.ThrowIfNull(dragDispatchResult);

        if (!string.IsNullOrWhiteSpace(dragDispatchResult.FailureStageHint))
        {
            return dragDispatchResult.FailureStageHint;
        }

        return dragDispatchResult.CommittedSideEffects
            ? InputFailureStageValues.DragDispatchPartialUncompensated
            : InputFailureStageValues.DragDispatchNotStartedAfterMove;
    }
}
