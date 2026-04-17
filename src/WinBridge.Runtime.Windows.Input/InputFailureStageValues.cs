namespace WinBridge.Runtime.Windows.Input;

internal static class InputFailureStageValues
{
    public const string RequestValidation = "request_validation";
    public const string TargetResolution = "target_resolution";
    public const string TargetPreflight = "target_preflight";
    public const string CoordinateMapping = "coordinate_mapping";
    public const string CursorMove = "cursor_move";
    public const string InputDispatch = "input_dispatch";
    public const string ClickDispatchCleanFailure = "click_dispatch_clean_failure";
    public const string ClickDispatchPartialCompensated = "click_dispatch_partial_compensated";
    public const string ClickDispatchPartialUncompensated = "click_dispatch_partial_uncompensated";
    public const string RuntimeUnhandledAfterCommittedSideEffect = "runtime_unhandled_after_committed_side_effect";
    public const string RuntimeUnhandledAfterCompletedActions = "runtime_unhandled_after_completed_actions";
    public const string CancellationAfterCommittedSideEffect = "cancellation_after_committed_side_effect";
    public const string CancellationAfterBatchCompleted = "cancellation_after_batch_completed";
    public const string ArtifactWrite = "artifact_write";
}
