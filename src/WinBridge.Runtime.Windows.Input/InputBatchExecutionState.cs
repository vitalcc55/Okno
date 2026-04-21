using System.Diagnostics.CodeAnalysis;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class InputBatchExecutionState
{
    private readonly List<InputActionResult> actionResults = [];
    private readonly long? initialTargetHwnd;

    public InputBatchExecutionState(
        WindowDescriptor expectedTargetWindow,
        string targetSource,
        InputProcessSecurityContext currentProcessSecurity)
    {
        ArgumentNullException.ThrowIfNull(expectedTargetWindow);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSource);

        ExpectedTargetWindow = expectedTargetWindow;
        TargetSource = targetSource;
        CurrentProcessSecurity = currentProcessSecurity;
        initialTargetHwnd = expectedTargetWindow.Hwnd;
    }

    public string TargetSource { get; }

    public WindowDescriptor ExpectedTargetWindow { get; private set; }

    public InputProcessSecurityContext CurrentProcessSecurity { get; }

    public int CompletedActionCount { get; private set; }

    public IReadOnlyList<InputActionResult> ActionResults => actionResults;

    public InputActionExecutionState? CurrentAction { get; private set; }

    public InputCommittedSideEffectContext? CommittedSideEffectContext { get; private set; }

    public bool HasCommittedSideEffectForCurrentAction =>
        CurrentAction is not null
        && CommittedSideEffectContext is not null
        && CommittedSideEffectContext.ActionIndex == CurrentAction.ActionIndex;

    public void BeginAction(int actionIndex, InputAction action, string? effectiveButton)
    {
        ArgumentNullException.ThrowIfNull(action);

        CurrentAction = new(
            ActionIndex: actionIndex,
            Action: action,
            Phase: InputIrreversiblePhase.None,
            ResolvedScreenPoint: null,
            EffectiveButton: effectiveButton,
            TargetHwnd: ExpectedTargetWindow.Hwnd);
    }

    public void UpdateExpectedTarget(WindowDescriptor targetWindow)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ExpectedTargetWindow = targetWindow;
    }

    public void UpdateResolvedPoint(InputPoint? resolvedScreenPoint)
    {
        InputActionExecutionState current = EnsureCurrentAction();
        CurrentAction = current with { ResolvedScreenPoint = resolvedScreenPoint };
    }

    public void UpdateTargetHwnd(long? targetHwnd)
    {
        InputActionExecutionState current = EnsureCurrentAction();
        CurrentAction = current with { TargetHwnd = targetHwnd };
    }

    public void RecordCommittedSideEffect(InputIrreversiblePhase phase)
    {
        InputActionExecutionState current = EnsureCurrentAction();
        if (current.ResolvedScreenPoint is not InputPoint resolvedScreenPoint
            && ActionRequiresResolvedPoint(current.Action))
        {
            throw new InvalidOperationException("Committed side effect требует authoritative resolved screen point.");
        }

        CurrentAction = current with { Phase = phase };
        CommittedSideEffectContext = new(
            ActionIndex: current.ActionIndex,
            Action: current.Action,
            Phase: phase,
            ResolvedScreenPoint: current.ResolvedScreenPoint,
            Button: current.EffectiveButton,
            TargetHwnd: current.TargetHwnd);
    }

    public void CompleteCurrentActionSuccess()
    {
        InputActionExecutionState current = EnsureCurrentAction();
        if (current.ResolvedScreenPoint is not InputPoint resolvedScreenPoint
            && ActionRequiresResolvedPoint(current.Action))
        {
            throw new InvalidOperationException("Successful action result требует authoritative resolved screen point.");
        }

        actionResults.Add(CreateSuccessfulActionResult(current.Action, current.ResolvedScreenPoint, current.EffectiveButton));
        CompletedActionCount++;
        ResetCurrentAction();
    }

    public void ResetCurrentAction()
    {
        CurrentAction = null;
    }

    public InputPoint? ResolveFailureResolvedScreenPoint()
    {
        InputActionExecutionState? current = CurrentAction;
        if (current is null)
        {
            return null;
        }

        return CommittedSideEffectContext is not null && CommittedSideEffectContext.ActionIndex == current.ActionIndex
            ? CommittedSideEffectContext.ResolvedScreenPoint
            : current.ResolvedScreenPoint;
    }

    public InputResult MaterializeCurrentActionFailure(
        string failureCode,
        string reason,
        long? targetHwnd)
    {
        InputActionExecutionState current = EnsureCurrentAction();
        actionResults.Add(CreateFailedActionResult(
            current.Action,
            failureCode,
            reason,
            ResolveFailureResolvedScreenPoint(),
            current.EffectiveButton));

        return CreateFailureResult(
            failureCode,
            reason,
            targetHwnd,
            failedActionIndex: current.ActionIndex);
    }

    public bool TryMaterializeCancellationBetweenActions(
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out InputResult? cancellationResult) =>
        TryMaterializeCancellation(
            InputCancellationObservationContext.BetweenActions,
            cancellationToken,
            out cancellationResult);

    public bool TryEnterActionSideEffectPhase(
        CancellationToken cancellationToken,
        [NotNullWhen(false)] out InputResult? cancellationResult)
    {
        if (TryMaterializeCancellationBetweenActions(cancellationToken, out cancellationResult))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        cancellationResult = null;
        return true;
    }

    public bool TryMaterializeCancellationAfterCommittedSideEffect(
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out InputResult? cancellationResult) =>
        TryMaterializeCancellation(
            InputCancellationObservationContext.InFlightAction,
            cancellationToken,
            out cancellationResult);

    public bool TryMaterializeCancellationAfterBatchCompleted(
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out InputResult? cancellationResult) =>
        TryMaterializeCancellation(
            InputCancellationObservationContext.AfterBatchCompletedBeforeSuccessReturn,
            cancellationToken,
            out cancellationResult);

    public InputResult MaterializeExceptionCancellation(CancellationToken cancellationToken)
    {
        if (CommittedSideEffectContext is null)
        {
            throw new InvalidOperationException("Exception cancellation materialization требует committed side effect context.");
        }

        InputCancellationObservationContext observationContext =
            CurrentAction?.Phase == InputIrreversiblePhase.None || CurrentAction is null
                ? InputCancellationObservationContext.BetweenActions
                : InputCancellationObservationContext.InFlightAction;

        if (!InputCancellationMaterializationPolicy.TryCreate(
                CommittedSideEffectContext,
                observationContext,
                cancellationToken,
                out InputCancellationMaterializationDecision? decision))
        {
            throw new InvalidOperationException("Exception cancellation materialization требует active cancellation decision.");
        }

        return MaterializeCancellationResult(decision);
    }

    public InputResult MaterializeUnexpectedFailureAfterCommittedSideEffect()
    {
        InputActionExecutionState current = EnsureCurrentAction();
        if (!HasCommittedSideEffectForCurrentAction)
        {
            throw new InvalidOperationException("Unexpected failure after side effect требует committed side effect context текущего action.");
        }

        const string reason = "Runtime столкнулся с unexpected failure после committed input side effect; retry без явной проверки результата небезопасен.";

        actionResults.Add(CreateFailedActionResult(
            current.Action,
            InputFailureCodeValues.InputDispatchFailed,
            reason,
            ResolveFailureResolvedScreenPoint(),
            CommittedSideEffectContext!.Button));

        return new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: reason,
            TargetHwnd: current.TargetHwnd ?? initialTargetHwnd,
            TargetSource: TargetSource,
            CompletedActionCount: CompletedActionCount,
            FailedActionIndex: current.ActionIndex,
            Actions: actionResults);
    }

    public InputResult MaterializeUnexpectedFailureAfterCompletedActions()
    {
        if (CompletedActionCount <= 0)
        {
            throw new InvalidOperationException("Batch-level unexpected failure materialization требует хотя бы один completed action.");
        }

        const string reason = "Runtime столкнулся с unexpected failure before the next input side effect; previously completed actions may already be visible, so retrying the full batch without verification is unsafe.";
        return new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: InputFailureCodeValues.InputDispatchFailed,
            Reason: reason,
            TargetHwnd: initialTargetHwnd,
            TargetSource: TargetSource,
            CompletedActionCount: CompletedActionCount,
            FailedActionIndex: null,
            Actions: actionResults);
    }

    public InputResult CreateInitialFailureResult(
        string failureCode,
        string reason,
        long? targetHwnd = null,
        int? failedActionIndex = null) =>
        CreateFailureResult(
            failureCode,
            reason,
            targetHwnd ?? initialTargetHwnd,
            failedActionIndex: failedActionIndex);

    public InputResult CreateFinalVerifyNeededResult() =>
        new(
            Status: InputStatusValues.VerifyNeeded,
            Decision: InputStatusValues.VerifyNeeded,
            ResultMode: InputResultModeValues.DispatchOnly,
            TargetHwnd: initialTargetHwnd,
            TargetSource: TargetSource,
            CompletedActionCount: CompletedActionCount,
            Actions: actionResults);

    private bool TryMaterializeCancellation(
        InputCancellationObservationContext observationContext,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out InputResult? cancellationResult)
    {
        if (!InputCancellationMaterializationPolicy.TryCreate(
                CommittedSideEffectContext,
                observationContext,
                cancellationToken,
                out InputCancellationMaterializationDecision? decision))
        {
            cancellationResult = null;
            return false;
        }

        cancellationResult = MaterializeCancellationResult(decision);
        return true;
    }

    private InputResult MaterializeCancellationResult(InputCancellationMaterializationDecision decision)
    {
        InputCommittedSideEffectContext committedContext = CommittedSideEffectContext
            ?? throw new InvalidOperationException("Cancellation materialization требует committed side effect context.");

        if (decision.ShouldAppendFailedAction)
        {
            actionResults.Add(CreateFailedActionResult(
                committedContext.Action,
                decision.FailureCode,
                decision.Reason,
                committedContext.ResolvedScreenPoint,
                committedContext.Button));
        }

        return CreateFailureResult(
            decision.FailureCode,
            decision.Reason,
            committedContext.TargetHwnd,
            failedActionIndex: decision.FailedActionIndex);
    }

    private InputResult CreateFailureResult(
        string failureCode,
        string reason,
        long? targetHwnd,
        int? failedActionIndex = null) =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: failureCode,
            Reason: reason,
            TargetHwnd: targetHwnd,
            TargetSource: TargetSource,
            CompletedActionCount: CompletedActionCount,
            FailedActionIndex: failedActionIndex,
            Actions: actionResults);

    private InputActionExecutionState EnsureCurrentAction() =>
        CurrentAction ?? throw new InvalidOperationException("Current action state не инициализирован.");

    private static InputActionResult CreateSuccessfulActionResult(
        InputAction action,
        InputPoint? resolvedScreenPoint,
        string? button) =>
        new(
            Type: action.Type,
            Status: InputStatusValues.VerifyNeeded,
            ResultMode: InputResultModeValues.DispatchOnly,
            CoordinateSpace: action.CoordinateSpace,
            RequestedPoint: action.Point,
            ResolvedScreenPoint: resolvedScreenPoint,
            Button: button,
            Keys: ResolveActionKeys(action));

    private static InputActionResult CreateFailedActionResult(
        InputAction action,
        string failureCode,
        string reason,
        InputPoint? resolvedScreenPoint = null,
        string? button = null) =>
        new(
            Type: action.Type,
            Status: InputStatusValues.Failed,
            FailureCode: failureCode,
            Reason: reason,
            CoordinateSpace: action.CoordinateSpace,
            RequestedPoint: action.Point,
            ResolvedScreenPoint: resolvedScreenPoint,
            Button: button ?? action.Button,
            Keys: ResolveActionKeys(action));

    private static IReadOnlyList<string>? ResolveActionKeys(InputAction action)
    {
        if (action.Keys is { Count: > 0 })
        {
            return action.Keys;
        }

        return string.IsNullOrWhiteSpace(action.Key)
            ? null
            : [action.Key];
    }

    private static bool ActionRequiresResolvedPoint(InputAction action) =>
        action.Point is not null
        || action.Path is not null
        || string.Equals(action.Type, InputActionTypeValues.Move, StringComparison.Ordinal)
        || string.Equals(action.Type, InputActionTypeValues.Click, StringComparison.Ordinal)
        || string.Equals(action.Type, InputActionTypeValues.DoubleClick, StringComparison.Ordinal)
        || string.Equals(action.Type, InputActionTypeValues.Scroll, StringComparison.Ordinal)
        || string.Equals(action.Type, InputActionTypeValues.Drag, StringComparison.Ordinal);
}
