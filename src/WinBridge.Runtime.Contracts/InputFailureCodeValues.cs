namespace WinBridge.Runtime.Contracts;

public static class InputFailureCodeValues
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedActionType = "unsupported_action_type";
    public const string UnsupportedCoordinateSpace = "unsupported_coordinate_space";
    public const string MissingTarget = "missing_target";
    public const string StaleExplicitTarget = "stale_explicit_target";
    public const string StaleAttachedTarget = "stale_attached_target";
    public const string TargetNotForeground = "target_not_foreground";
    public const string TargetMinimized = "target_minimized";
    public const string TargetIntegrityBlocked = "target_integrity_blocked";
    public const string CaptureReferenceRequired = "capture_reference_required";
    public const string CaptureReferenceStale = "capture_reference_stale";
    public const string PointOutOfBounds = "point_out_of_bounds";
    public const string CursorMoveFailed = "cursor_move_failed";
    public const string InputDispatchFailed = "input_dispatch_failed";
}
