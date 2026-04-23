using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinFailureCodeMapper
{
    public static string? ToPublicFailureCode(string? failureCode) =>
        failureCode switch
        {
            null or "" => null,
            InputFailureCodeValues.InvalidRequest => ComputerUseWinFailureCodeValues.InvalidRequest,
            InputFailureCodeValues.UnsupportedCoordinateSpace => ComputerUseWinFailureCodeValues.InvalidRequest,
            InputFailureCodeValues.UnsupportedActionType => ComputerUseWinFailureCodeValues.UnsupportedAction,
            InputFailureCodeValues.MissingTarget => ComputerUseWinFailureCodeValues.MissingTarget,
            InputFailureCodeValues.StaleExplicitTarget => ComputerUseWinFailureCodeValues.StaleState,
            InputFailureCodeValues.StaleAttachedTarget => ComputerUseWinFailureCodeValues.StaleState,
            InputFailureCodeValues.CaptureReferenceRequired => ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
            InputFailureCodeValues.CaptureReferenceStale => ComputerUseWinFailureCodeValues.StaleState,
            InputFailureCodeValues.TargetPreflightFailed => ComputerUseWinFailureCodeValues.TargetPreflightFailed,
            InputFailureCodeValues.TargetNotForeground => ComputerUseWinFailureCodeValues.TargetNotForeground,
            InputFailureCodeValues.TargetMinimized => ComputerUseWinFailureCodeValues.TargetMinimized,
            InputFailureCodeValues.TargetIntegrityBlocked => ComputerUseWinFailureCodeValues.TargetIntegrityBlocked,
            InputFailureCodeValues.PointOutOfBounds => ComputerUseWinFailureCodeValues.PointOutOfBounds,
            InputFailureCodeValues.CursorMoveFailed => ComputerUseWinFailureCodeValues.CursorMoveFailed,
            InputFailureCodeValues.InputDispatchFailed => ComputerUseWinFailureCodeValues.InputDispatchFailed,
            _ when IsPublicFailureCode(failureCode) => failureCode,
            _ => ComputerUseWinFailureCodeValues.InputDispatchFailed,
        };

    private static bool IsPublicFailureCode(string? failureCode) =>
        failureCode is
            ComputerUseWinFailureCodeValues.InvalidRequest or
            ComputerUseWinFailureCodeValues.MissingTarget or
            ComputerUseWinFailureCodeValues.AmbiguousTarget or
            ComputerUseWinFailureCodeValues.ApprovalRequired or
            ComputerUseWinFailureCodeValues.BlockedTarget or
            ComputerUseWinFailureCodeValues.StateRequired or
            ComputerUseWinFailureCodeValues.StaleState or
            ComputerUseWinFailureCodeValues.ObservationFailed or
            ComputerUseWinFailureCodeValues.UnsupportedAction or
            ComputerUseWinFailureCodeValues.CaptureReferenceRequired or
            ComputerUseWinFailureCodeValues.TargetPreflightFailed or
            ComputerUseWinFailureCodeValues.TargetNotForeground or
            ComputerUseWinFailureCodeValues.TargetMinimized or
            ComputerUseWinFailureCodeValues.TargetIntegrityBlocked or
            ComputerUseWinFailureCodeValues.PointOutOfBounds or
            ComputerUseWinFailureCodeValues.CursorMoveFailed or
            ComputerUseWinFailureCodeValues.InputDispatchFailed;
}
