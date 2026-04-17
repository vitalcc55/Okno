using System.Diagnostics.CodeAnalysis;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class Win32InputService(
    IWindowTargetResolver windowTargetResolver,
    IInputPlatform platform,
    TimeProvider timeProvider) : IInputService
{
    private static readonly string[] SupportedActionTypes =
    [.. InputClickFirstSubsetContract.SupportedActionTypes];

    private static readonly TimeSpan DoubleClickDelay = TimeSpan.FromMilliseconds(50);

    public async Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!InputRequestValidator.TryValidateSupportedSubset(request, SupportedActionTypes, out string? failureCode, out string? reason))
        {
            return CreateFailureResult(
                failureCode ?? InputFailureCodeValues.InvalidRequest,
                reason ?? "Input request не прошёл validation.");
        }

        if (!InputClickFirstRuntimeSubsetPolicy.TryValidateRequest(request, out failureCode, out reason))
        {
            return CreateFailureResult(
                failureCode ?? InputFailureCodeValues.InvalidRequest,
                reason ?? "Input request не входит в click-first runtime subset Package B.");
        }

        await using IAsyncDisposable executionLease = await InputExecutionGate.EnterAsync(cancellationToken).ConfigureAwait(false);

        InputTargetResolution targetResolution = windowTargetResolver.ResolveInputTarget(request.Hwnd, context.AttachedWindow);
        if (targetResolution.Window is not WindowDescriptor targetWindow || string.IsNullOrWhiteSpace(targetResolution.Source))
        {
            return CreateFailureResult(
                targetResolution.FailureCode ?? InputFailureCodeValues.MissingTarget,
                CreateTargetFailureReason(targetResolution.FailureCode),
                targetSource: targetResolution.Source);
        }

        InputBatchExecutionState batch = new(
            targetWindow,
            targetResolution.Source,
            platform.ProbeCurrentProcessSecurity());

        try
        {
            for (int index = 0; index < request.Actions.Count; index++)
            {
                InputResult? cancellationResult;
                InputResult? shortCircuitResult;

                if (batch.TryMaterializeCancellationBetweenActions(cancellationToken, out cancellationResult))
                {
                    return cancellationResult;
                }

                cancellationToken.ThrowIfCancellationRequested();

                InputAction action = request.Actions[index];
                batch.BeginAction(index, action, ResolveEffectiveButtonForAction(action));

                if (!TryResolveAdmissibleTarget(
                        batch,
                        dispatchPlan: null,
                        out WindowDescriptor? liveTargetWindow,
                        out _,
                        out failureCode,
                        out reason))
                {
                    return batch.MaterializeCurrentActionFailure(
                        failureCode!,
                        reason!,
                        targetWindow.Hwnd);
                }

                batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);

                if (!InputCoordinateMapper.TryBuildDispatchPlan(action, liveTargetWindow, out InputPointerDispatchPlan? dispatchPlan, out failureCode, out reason)
                    || dispatchPlan is null)
                {
                    return batch.MaterializeCurrentActionFailure(
                        failureCode!,
                        reason!,
                        liveTargetWindow.Hwnd);
                }

                if (string.Equals(action.Type, InputActionTypeValues.DoubleClick, StringComparison.Ordinal))
                {
                    if (!TryPrepareDispatchPlan(
                            batch,
                            dispatchPlan,
                            InputDispatchPlanRefreshPolicy.AllowRefreshedPoint,
                            cancellationToken,
                            out shortCircuitResult,
                            out liveTargetWindow,
                            out dispatchPlan,
                            out failureCode,
                            out reason))
                    {
                        return shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            liveTargetWindow?.Hwnd ?? targetWindow.Hwnd);
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow!);
                    batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                }

                if (!batch.TryEnterActionSideEffectPhase(cancellationToken, out cancellationResult))
                {
                    return cancellationResult!;
                }

                CursorMoveAttemptResult moveResult = TryMoveCursorAndVerify(liveTargetWindow!, dispatchPlan!.ResolvedScreenPoint);
                ApplyMoveOutcomeToBatch(batch, dispatchPlan.ResolvedScreenPoint, moveResult);
                if (!moveResult.Success)
                {
                    if (moveResult.MoveApplied)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                    }

                    return batch.MaterializeCurrentActionFailure(
                        moveResult.FailureCode!,
                        moveResult.Reason!,
                        liveTargetWindow.Hwnd);
                }

                batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                {
                    return cancellationResult;
                }

                if (string.Equals(action.Type, InputActionTypeValues.Move, StringComparison.Ordinal))
                {
                    batch.UpdateExpectedTarget(liveTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                string button = ResolveEffectiveButton(action);
                if (string.Equals(action.Type, InputActionTypeValues.DoubleClick, StringComparison.Ordinal))
                {
                    if (!TryDispatchClickWithinBoundary(
                            batch,
                            dispatchPlan!,
                            InputDispatchPlanRefreshPolicy.RequireStablePoint,
                            InputButtonValues.Left,
                            cancellationToken,
                            out shortCircuitResult,
                            out liveTargetWindow,
                            out dispatchPlan,
                            out failureCode,
                            out reason))
                    {
                        return shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            liveTargetWindow?.Hwnd ?? targetWindow.Hwnd);
                    }

                    batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                    batch.UpdateResolvedPoint(dispatchPlan!.ResolvedScreenPoint);
                    batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDoubleClickFirstTap);
                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return cancellationResult;
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow);
                    await Task.Delay(DoubleClickDelay, timeProvider, cancellationToken).ConfigureAwait(false);

                    if (!TryDispatchClickWithinBoundary(
                            batch,
                            dispatchPlan,
                            InputDispatchPlanRefreshPolicy.RequireStablePoint,
                            InputButtonValues.Left,
                            cancellationToken,
                            out shortCircuitResult,
                            out liveTargetWindow,
                            out dispatchPlan,
                            out failureCode,
                            out reason))
                    {
                        return shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            liveTargetWindow?.Hwnd ?? targetWindow.Hwnd);
                    }

                    batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                    batch.UpdateResolvedPoint(dispatchPlan!.ResolvedScreenPoint);
                    batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDoubleClickSecondTap);
                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return cancellationResult;
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                if (!TryDispatchClickWithinBoundary(
                        batch,
                        dispatchPlan!,
                        InputDispatchPlanRefreshPolicy.AllowRefreshedPoint,
                        button,
                        cancellationToken,
                        out shortCircuitResult,
                        out liveTargetWindow,
                        out dispatchPlan,
                        out failureCode,
                        out reason))
                {
                    return shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                        failureCode!,
                        reason!,
                        liveTargetWindow?.Hwnd ?? targetWindow.Hwnd);
                }

                batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                batch.UpdateResolvedPoint(dispatchPlan!.ResolvedScreenPoint);
                batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterClickTap);
                if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                {
                    return cancellationResult;
                }

                batch.UpdateExpectedTarget(liveTargetWindow);
                batch.CompleteCurrentActionSuccess();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && batch.CommittedSideEffectContext is not null)
        {
            return batch.MaterializeExceptionCancellation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (batch.HasCommittedSideEffectForCurrentAction)
        {
            throw new InputExecutionFailureException(
                batch.MaterializeUnexpectedFailureAfterCommittedSideEffect(),
                exception);
        }
        catch (Exception exception) when (batch.CompletedActionCount > 0)
        {
            throw new InputExecutionFailureException(
                batch.MaterializeUnexpectedFailureAfterCompletedActions(),
                exception);
        }

        if (batch.TryMaterializeCancellationAfterBatchCompleted(cancellationToken, out InputResult? finalCancellationResult))
        {
            return finalCancellationResult;
        }

        return batch.CreateFinalVerifyNeededResult();
    }

    private CursorMoveAttemptResult TryMoveCursorAndVerify(
        WindowDescriptor admittedTargetWindow,
        InputPoint resolvedScreenPoint)
    {
        InputPointerSideEffectBoundaryResult boundaryResult = platform.ValidatePointerSideEffectBoundary(admittedTargetWindow);
        if (!boundaryResult.Success)
        {
            return new(
                Success: false,
                MoveApplied: false,
                ObservedScreenPoint: null,
                FailureCode: boundaryResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                Reason: boundaryResult.Reason ?? "Runtime не смог доказать safe pointer side-effect boundary перед SetCursorPos.");
        }

        if (!platform.TrySetCursorPosition(resolvedScreenPoint))
        {
            return new(
                Success: false,
                MoveApplied: false,
                ObservedScreenPoint: null,
                FailureCode: InputFailureCodeValues.CursorMoveFailed,
                Reason: "SetCursorPos вернул failure для requested screen point.");
        }

        if (!platform.TryGetCursorPosition(out InputPoint currentCursorPoint))
        {
            return new(
                Success: false,
                MoveApplied: true,
                ObservedScreenPoint: null,
                FailureCode: InputFailureCodeValues.CursorMoveFailed,
                Reason: "Runtime не смог подтвердить cursor position через GetCursorPos.");
        }

        if (!Equals(currentCursorPoint, resolvedScreenPoint))
        {
            return new(
                Success: false,
                MoveApplied: true,
                ObservedScreenPoint: currentCursorPoint,
                FailureCode: InputFailureCodeValues.CursorMoveFailed,
                Reason: $"GetCursorPos вернул ({currentCursorPoint.X},{currentCursorPoint.Y}) вместо ожидаемой точки ({resolvedScreenPoint.X},{resolvedScreenPoint.Y}).");
        }

        return new(
            Success: true,
            MoveApplied: true,
            ObservedScreenPoint: currentCursorPoint,
            FailureCode: null,
            Reason: null);
    }

    private bool TryDispatchClickWithinBoundary(
        InputBatchExecutionState batch,
        InputPointerDispatchPlan dispatchPlan,
        InputDispatchPlanRefreshPolicy refreshPolicy,
        string button,
        CancellationToken cancellationToken,
        out InputResult? shortCircuitResult,
        [NotNullWhen(true)] out WindowDescriptor? liveTargetWindow,
        out InputPointerDispatchPlan? validatedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        shortCircuitResult = null;

        if (!TryResolveAdmissibleTarget(
                batch,
                dispatchPlan,
                out liveTargetWindow,
                out validatedDispatchPlan,
                out failureCode,
                out reason))
        {
            return false;
        }

        batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);

        if (!TryAcceptValidatedDispatchPlan(
                batch,
                liveTargetWindow,
                dispatchPlan,
                validatedDispatchPlan!,
                refreshPolicy,
                moveCursorWhenRefreshed: true,
                cancellationToken,
                out shortCircuitResult,
                out validatedDispatchPlan,
                out failureCode,
                out reason))
        {
            return false;
        }

        InputClickDispatchResult dispatchResult = platform.DispatchClick(
            new InputClickDispatchContext(
                validatedDispatchPlan!.ResolvedScreenPoint,
                button,
                liveTargetWindow));
        if (!dispatchResult.Success)
        {
            failureCode = dispatchResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed;
            reason = dispatchResult.Reason ?? $"Button dispatch для '{button}' не был подтверждён платформой.";
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private bool TryPrepareDispatchPlan(
        InputBatchExecutionState batch,
        InputPointerDispatchPlan dispatchPlan,
        InputDispatchPlanRefreshPolicy refreshPolicy,
        CancellationToken cancellationToken,
        out InputResult? shortCircuitResult,
        [NotNullWhen(true)] out WindowDescriptor? liveTargetWindow,
        out InputPointerDispatchPlan? preparedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        shortCircuitResult = null;

        if (!TryResolveAdmissibleTarget(
                batch,
                dispatchPlan,
                out liveTargetWindow,
                out InputPointerDispatchPlan? validatedDispatchPlan,
                out failureCode,
                out reason))
        {
            preparedDispatchPlan = null;
            return false;
        }

        batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);

        if (!TryAcceptValidatedDispatchPlan(
                batch,
                liveTargetWindow,
                dispatchPlan,
                validatedDispatchPlan!,
                refreshPolicy,
                moveCursorWhenRefreshed: false,
                cancellationToken,
                out shortCircuitResult,
                out preparedDispatchPlan,
                out failureCode,
                out reason))
        {
            return false;
        }

        return true;
    }

    private bool TryAcceptValidatedDispatchPlan(
        InputBatchExecutionState batch,
        WindowDescriptor admittedTargetWindow,
        InputPointerDispatchPlan originalDispatchPlan,
        InputPointerDispatchPlan refreshedDispatchPlan,
        InputDispatchPlanRefreshPolicy refreshPolicy,
        bool moveCursorWhenRefreshed,
        CancellationToken cancellationToken,
        out InputResult? shortCircuitResult,
        out InputPointerDispatchPlan? acceptedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        shortCircuitResult = null;

        if (!SamePoint(originalDispatchPlan.ResolvedScreenPoint, refreshedDispatchPlan.ResolvedScreenPoint))
        {
            if (refreshPolicy == InputDispatchPlanRefreshPolicy.RequireStablePoint)
            {
                acceptedDispatchPlan = null;
                failureCode = InputFailureCodeValues.CaptureReferenceStale;
                reason = "Gesture требует сохранить одну и ту же resolved screen point; boundary refresh потребовал бы retarget.";
                return false;
            }

            if (moveCursorWhenRefreshed
                && batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out shortCircuitResult))
            {
                acceptedDispatchPlan = null;
                failureCode = shortCircuitResult.FailureCode;
                reason = shortCircuitResult.Reason;
                return false;
            }

            if (moveCursorWhenRefreshed)
            {
                CursorMoveAttemptResult refreshedMoveResult = TryMoveCursorAndVerify(
                    admittedTargetWindow,
                    refreshedDispatchPlan.ResolvedScreenPoint);
                ApplyMoveOutcomeToBatch(batch, refreshedDispatchPlan.ResolvedScreenPoint, refreshedMoveResult);
                if (!refreshedMoveResult.Success)
                {
                    if (refreshedMoveResult.MoveApplied)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                    }

                    acceptedDispatchPlan = null;
                    failureCode = refreshedMoveResult.FailureCode;
                    reason = refreshedMoveResult.Reason;
                    return false;
                }
            }

            if (moveCursorWhenRefreshed)
            {
                batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
            }

            if (moveCursorWhenRefreshed
                && batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out shortCircuitResult))
            {
                acceptedDispatchPlan = null;
                failureCode = shortCircuitResult.FailureCode;
                reason = shortCircuitResult.Reason;
                return false;
            }
        }

        acceptedDispatchPlan = refreshedDispatchPlan;
        failureCode = null;
        reason = null;
        return true;
    }

    private bool TryResolveAdmissibleTarget(
        InputBatchExecutionState batch,
        InputPointerDispatchPlan? dispatchPlan,
        [NotNullWhen(true)] out WindowDescriptor? liveTargetWindow,
        out InputPointerDispatchPlan? validatedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        liveTargetWindow = windowTargetResolver.ResolveLiveWindowByIdentity(batch.ExpectedTargetWindow);
        validatedDispatchPlan = dispatchPlan;
        if (liveTargetWindow is null)
        {
            failureCode = MapStaleTargetFailureCode(batch.TargetSource);
            reason = CreateTargetFailureReason(failureCode);
            validatedDispatchPlan = null;
            return false;
        }

        if (dispatchPlan is not null
            && !InputCoordinateMapper.TryValidateDispatchPlan(dispatchPlan, liveTargetWindow, out validatedDispatchPlan, out failureCode, out reason))
        {
            return false;
        }

        InputTargetSecurityInfo targetSecurity = platform.ProbeTargetSecurity(liveTargetWindow.Hwnd, liveTargetWindow.ProcessId);
        InputTargetPreflightResult preflight = InputTargetPreflightPolicy.Evaluate(
            batch.TargetSource,
            liveTargetWindow,
            batch.CurrentProcessSecurity,
            targetSecurity);
        if (!preflight.IsAllowed)
        {
            failureCode = preflight.FailureCode;
            reason = preflight.Reason;
            validatedDispatchPlan = null;
            return false;
        }

        failureCode = null;
        reason = null;
        return true;
    }

    private static bool SamePoint(InputPoint left, InputPoint right) =>
        left.X == right.X && left.Y == right.Y;

    private static string ResolveEffectiveButton(InputAction action) =>
        string.IsNullOrWhiteSpace(action.Button) ? InputButtonValues.Left : action.Button!;

    private static string? ResolveEffectiveButtonForAction(InputAction action) =>
        action.Type switch
        {
            InputActionTypeValues.Move => null,
            InputActionTypeValues.DoubleClick => InputButtonValues.Left,
            InputActionTypeValues.Click => ResolveEffectiveButton(action),
            _ => action.Button,
        };

    private static void ApplyMoveOutcomeToBatch(
        InputBatchExecutionState batch,
        InputPoint plannedScreenPoint,
        CursorMoveAttemptResult moveResult)
    {
        if (!moveResult.MoveApplied)
        {
            return;
        }

        batch.UpdateResolvedPoint(moveResult.ObservedScreenPoint ?? plannedScreenPoint);
    }

    private readonly record struct CursorMoveAttemptResult(
        bool Success,
        bool MoveApplied,
        InputPoint? ObservedScreenPoint,
        string? FailureCode,
        string? Reason);

    private static string MapStaleTargetFailureCode(string? targetSource) =>
        string.Equals(targetSource, InputTargetSourceValues.Attached, StringComparison.Ordinal)
            ? InputFailureCodeValues.StaleAttachedTarget
            : InputFailureCodeValues.StaleExplicitTarget;

    private static string CreateTargetFailureReason(string? failureCode) =>
        failureCode switch
        {
            InputFailureCodeValues.StaleExplicitTarget => "Explicit target больше не совпадает с live window identity.",
            InputFailureCodeValues.StaleAttachedTarget => "Attached target больше не совпадает с live window identity.",
            InputFailureCodeValues.MissingTarget => "windows.input Package B требует explicit или attached target без active fallback.",
            _ => "Runtime не смог разрешить target для windows.input.",
        };

    private static InputResult CreateFailureResult(
        string failureCode,
        string reason,
        long? targetHwnd = null,
        string? targetSource = null,
        int completedActionCount = 0,
        int? failedActionIndex = null,
        IReadOnlyList<InputActionResult>? actions = null) =>
        new(
            Status: InputStatusValues.Failed,
            Decision: InputStatusValues.Failed,
            FailureCode: failureCode,
            Reason: reason,
            TargetHwnd: targetHwnd,
            TargetSource: targetSource,
            CompletedActionCount: completedActionCount,
            FailedActionIndex: failedActionIndex,
            Actions: actions);
}
