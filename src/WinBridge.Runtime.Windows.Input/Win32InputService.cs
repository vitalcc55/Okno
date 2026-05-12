// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Windows.Input;

internal sealed class Win32InputService(
    IWindowTargetResolver windowTargetResolver,
    IInputPlatform platform,
    TimeProvider timeProvider,
    InputResultMaterializer? resultMaterializer = null,
    InputExecutionOptions? executionOptions = null) : IInputService
{
    private static readonly string[] ClickFirstSupportedActionTypes =
    [.. InputClickFirstSubsetContract.SupportedActionTypes];
    private static readonly string[] ComputerUseCoreSupportedActionTypes =
    [.. InputActionTypeValues.StructuralFreeze];

    private readonly InputExecutionOptions effectiveExecutionOptions = executionOptions ?? InputExecutionOptions.Default;

    public async Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        CancellationToken cancellationToken)
        => await ExecuteAsync(request, context, InputExecutionProfileValues.ClickFirstPublic, cancellationToken).ConfigureAwait(false);

    public async Task<InputResult> ExecuteAsync(
        InputRequest request,
        InputExecutionContext context,
        string executionProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        InputBatchExecutionState? batch = null;

        InputResult Materialize(InputResult result, string? failureStage = null, Exception? failureException = null) =>
            resultMaterializer is null
                ? result
                : resultMaterializer.Materialize(
                    request,
                    context,
                    result,
                    failureStage,
                    failureException,
                    batch?.CommittedSideEffectContext);

        InputResult MaterializeFactual(InputResult result, string? failureStage = null, Exception? failureException = null) =>
            Materialize(result, failureStage ?? InputFailureStagePolicy.ResolveFailureStage(result.FailureCode), failureException);

        if (!TryResolveSupportedActionTypes(executionProfile, out string[] supportedActionTypes, out string? failureCode, out string? reason))
        {
            return MaterializeFactual(CreateFailureResult(
                failureCode ?? InputFailureCodeValues.InvalidRequest,
                reason ?? "Input execution profile не поддерживается runtime."));
        }

        if (!InputRequestValidator.TryValidateSupportedSubset(request, supportedActionTypes, out failureCode, out reason))
        {
            return MaterializeFactual(CreateFailureResult(
                failureCode ?? InputFailureCodeValues.InvalidRequest,
                reason ?? "Input request не прошёл validation."));
        }

        if (string.Equals(executionProfile, InputExecutionProfileValues.ClickFirstPublic, StringComparison.Ordinal)
            && !InputClickFirstRuntimeSubsetPolicy.TryValidateRequest(request, out failureCode, out reason))
        {
            return MaterializeFactual(CreateFailureResult(
                failureCode ?? InputFailureCodeValues.InvalidRequest,
                reason ?? "Input request не входит в click-first runtime subset Package B."));
        }

        await using IAsyncDisposable executionLease = await InputExecutionGate.EnterAsync(cancellationToken).ConfigureAwait(false);

        InputTargetResolution targetResolution = windowTargetResolver.ResolveInputTarget(request.Hwnd, context.AttachedWindow);
        if (targetResolution.Window is not WindowDescriptor targetWindow || string.IsNullOrWhiteSpace(targetResolution.Source))
        {
            return MaterializeFactual(CreateFailureResult(
                targetResolution.FailureCode ?? InputFailureCodeValues.MissingTarget,
                InputTargetFailurePolicy.CreateTargetFailureReason(targetResolution.FailureCode),
                targetSource: targetResolution.Source));
        }

        batch = new(
            targetWindow,
            targetResolution.Source,
            platform.ProbeCurrentProcessSecurity());
        InputTargetSecurityProbeCache targetSecurityProbeCache = new(platform);

        try
        {
            for (int index = 0; index < request.Actions.Count; index++)
            {
                InputResult? cancellationResult;
                InputResult? shortCircuitResult;

                if (batch.TryMaterializeCancellationBetweenActions(cancellationToken, out cancellationResult))
                {
                    return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                }

                cancellationToken.ThrowIfCancellationRequested();

                InputAction action = request.Actions[index];
                batch.BeginAction(index, action, InputActionSemantics.ResolveEffectiveButtonForAction(action));

                if (InputActionSemantics.IsType(action))
                {
                    if (!TryResolveAdmissibleTarget(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan: null,
                            out WindowDescriptor? keyboardTargetWindow,
                            out _,
                            out failureCode,
                            out reason))
                    {
                        return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            targetWindow.Hwnd));
                    }

                    batch.UpdateTargetHwnd(keyboardTargetWindow!.Hwnd);
                    if (!batch.TryEnterDispatchBoundary(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult!, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    InputDispatchResult dispatchResult = platform.DispatchText(
                        new InputTextDispatchContext(
                            action.Text!,
                            keyboardTargetWindow));
                    if (!dispatchResult.Success)
                    {
                        if (dispatchResult.CommittedSideEffects)
                        {
                            batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterTypeTextDispatch);
                        }

                        return MaterializeFactual(
                            batch.MaterializeCurrentActionFailure(
                                dispatchResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                                dispatchResult.Reason ?? "Runtime не смог подтвердить text dispatch.",
                                keyboardTargetWindow.Hwnd),
                            dispatchResult.CommittedSideEffects
                                ? InputFailureStageValues.TextDispatchCommittedFailure
                                : InputFailureStageValues.InputDispatch);
                    }

                    if (dispatchResult.CommittedSideEffects)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterTypeTextDispatch);
                    }

                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    batch.UpdateExpectedTarget(keyboardTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                if (InputActionSemantics.IsKeypress(action))
                {
                    if (!TryResolveAdmissibleTarget(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan: null,
                            out WindowDescriptor? keyboardTargetWindow,
                            out _,
                            out failureCode,
                            out reason))
                    {
                        return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            targetWindow.Hwnd));
                    }

                    batch.UpdateTargetHwnd(keyboardTargetWindow!.Hwnd);
                    if (!batch.TryEnterDispatchBoundary(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult!, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    InputDispatchResult dispatchResult = platform.DispatchKeypress(
                        new InputKeypressDispatchContext(
                            action.Key!,
                            action.Repeat ?? 1,
                            keyboardTargetWindow));
                    if (!dispatchResult.Success)
                    {
                        if (dispatchResult.CommittedSideEffects)
                        {
                            batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterKeypressDispatch);
                        }

                        return MaterializeFactual(
                            batch.MaterializeCurrentActionFailure(
                                dispatchResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                                dispatchResult.Reason ?? "Runtime не смог подтвердить keypress dispatch.",
                                keyboardTargetWindow.Hwnd),
                            dispatchResult.FailureStageHint
                                ?? (dispatchResult.CommittedSideEffects
                                    ? InputFailureStageValues.KeypressDispatchCommittedFailure
                                    : InputFailureStageValues.InputDispatch));
                    }

                    if (dispatchResult.CommittedSideEffects)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterKeypressDispatch);
                    }

                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    batch.UpdateExpectedTarget(keyboardTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                if (InputActionSemantics.IsDrag(action))
                {
                    if (!TryResolveAdmissibleTarget(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan: null,
                            out WindowDescriptor? dragTargetWindow,
                            out _,
                            out failureCode,
                            out reason))
                    {
                        return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            targetWindow.Hwnd));
                    }

                    batch.UpdateTargetHwnd(dragTargetWindow!.Hwnd);

                    if (!InputCoordinateMapper.TryBuildDragDispatchPlan(action, dragTargetWindow, out InputDragDispatchPlan? dragDispatchPlan, out failureCode, out reason)
                        || dragDispatchPlan is null)
                    {
                        return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            dragTargetWindow.Hwnd));
                    }

                    if (!batch.TryEnterActionSideEffectPhase(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult!, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    InputPoint dragStartPoint = dragDispatchPlan.ResolvedScreenPath[0];
                    CursorMoveAttemptResult dragMoveResult = TryMoveCursorAndVerify(dragTargetWindow, dragStartPoint);
                    ApplyMoveOutcomeToBatch(batch, dragStartPoint, dragMoveResult);
                    if (!dragMoveResult.Success)
                    {
                        if (dragMoveResult.MoveApplied)
                        {
                            batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                        }

                        return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                            dragMoveResult.FailureCode!,
                            dragMoveResult.Reason!,
                            dragTargetWindow.Hwnd));
                    }

                    batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    if (!TryResolveAdmissibleTarget(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan: null,
                            out dragTargetWindow,
                            out _,
                            out failureCode,
                            out reason))
                    {
                        return MaterializeFactual(
                            batch.MaterializeCurrentActionFailure(
                                failureCode!,
                                reason!,
                                targetWindow.Hwnd),
                            InputFailureStagePolicy.ResolveFailureStage(failureCode));
                    }

                    batch.UpdateTargetHwnd(dragTargetWindow!.Hwnd);

                    if (!InputCoordinateMapper.TryValidateDragDispatchPlan(
                            dragDispatchPlan,
                            dragTargetWindow,
                            out dragDispatchPlan,
                            out failureCode,
                            out reason)
                        || dragDispatchPlan is null)
                    {
                        return MaterializeFactual(
                            batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            dragTargetWindow.Hwnd),
                            InputFailureStageValues.CoordinateMapping);
                    }

                    if (!batch.TryEnterDispatchBoundary(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult!, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    InputDispatchResult dragDispatchResult = platform.DispatchDrag(
                        new InputDragDispatchContext(
                            dragDispatchPlan.ResolvedScreenPath,
                            dragTargetWindow));
                    if (platform.TryGetCursorPosition(out InputPoint observedDragCursorPoint))
                    {
                        batch.UpdateResolvedPoint(observedDragCursorPoint);
                    }
                    else
                    {
                        batch.UpdateResolvedPoint(dragDispatchPlan.ResolvedScreenPath[^1]);
                    }

                    if (!dragDispatchResult.Success)
                    {
                        if (dragDispatchResult.CommittedSideEffects)
                        {
                            batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDragDispatch);
                        }

                        return MaterializeFactual(
                            batch.MaterializeCurrentActionFailure(
                                dragDispatchResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                                dragDispatchResult.Reason ?? "Runtime не смог подтвердить drag dispatch.",
                                dragTargetWindow.Hwnd),
                            InputFailureStagePolicy.ResolveDragFailureStage(dragDispatchResult));
                    }

                    if (dragDispatchResult.CommittedSideEffects)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDragDispatch);
                    }

                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    batch.UpdateExpectedTarget(dragTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                if (!TryResolveAdmissibleTarget(
                        batch,
                        targetSecurityProbeCache,
                        dispatchPlan: null,
                        out WindowDescriptor? liveTargetWindow,
                        out _,
                        out failureCode,
                        out reason))
                {
                    return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                        failureCode!,
                        reason!,
                        targetWindow.Hwnd));
                }

                batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);

                if (!InputCoordinateMapper.TryBuildDispatchPlan(action, liveTargetWindow, out InputPointerDispatchPlan? dispatchPlan, out failureCode, out reason)
                    || dispatchPlan is null)
                {
                    return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                        failureCode!,
                        reason!,
                        liveTargetWindow.Hwnd));
                }

                if (InputActionSemantics.IsDoubleClick(action))
                {
                    if (!TryPrepareDispatchPlan(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan,
                            InputDispatchPlanRefreshPolicy.AllowRefreshedPoint,
                            cancellationToken,
                            out shortCircuitResult,
                            out liveTargetWindow,
                            out dispatchPlan,
                            out failureCode,
                            out reason,
                            out string? preparationFailureStage))
                    {
                        return MaterializeFactual(
                            shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                                failureCode!,
                                reason!,
                                liveTargetWindow?.Hwnd ?? targetWindow.Hwnd),
                            preparationFailureStage);
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow!);
                    batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                }

                if (!batch.TryEnterActionSideEffectPhase(cancellationToken, out cancellationResult))
                {
                    return MaterializeFactual(cancellationResult!, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                }

                CursorMoveAttemptResult moveResult = TryMoveCursorAndVerify(liveTargetWindow!, dispatchPlan!.ResolvedScreenPoint);
                ApplyMoveOutcomeToBatch(batch, dispatchPlan.ResolvedScreenPoint, moveResult);
                if (!moveResult.Success)
                {
                    if (moveResult.MoveApplied)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                    }

                    return MaterializeFactual(batch.MaterializeCurrentActionFailure(
                        moveResult.FailureCode!,
                        moveResult.Reason!,
                        liveTargetWindow.Hwnd));
                }

                batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
                if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                {
                    return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                }

                if (InputActionSemantics.IsMove(action))
                {
                    batch.UpdateExpectedTarget(liveTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                if (InputActionSemantics.IsScroll(action))
                {
                    if (!batch.TryEnterDispatchBoundary(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult!, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    InputDispatchResult dispatchResult = platform.DispatchScroll(
                        new InputScrollDispatchContext(
                            dispatchPlan!.ResolvedScreenPoint,
                            action.Direction!,
                            action.Delta!.Value,
                            liveTargetWindow));
                    if (!dispatchResult.Success)
                    {
                        if (dispatchResult.CommittedSideEffects)
                        {
                            batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterScrollDispatch);
                        }

                        return MaterializeFactual(
                            batch.MaterializeCurrentActionFailure(
                                dispatchResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed,
                                dispatchResult.Reason ?? "Runtime не смог подтвердить scroll dispatch.",
                                liveTargetWindow.Hwnd),
                            InputFailureStageValues.InputDispatch);
                    }

                    if (dispatchResult.CommittedSideEffects)
                    {
                        batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterScrollDispatch);
                    }

                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                string button = InputActionSemantics.ResolveEffectiveButton(action);
                if (InputActionSemantics.IsDoubleClick(action))
                {
                    if (!TryDispatchClickWithinBoundary(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan!,
                            InputDispatchPlanRefreshPolicy.RequireStablePoint,
                            InputButtonValues.Left,
                            InputIrreversiblePhase.AfterDoubleClickFirstTap,
                            cancellationToken,
                            out shortCircuitResult,
                            out liveTargetWindow,
                            out dispatchPlan,
                            out failureCode,
                            out reason,
                            out string? failureStage))
                    {
                        return MaterializeFactual(
                            shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                                failureCode!,
                                reason!,
                                liveTargetWindow?.Hwnd ?? targetWindow.Hwnd),
                            failureStage);
                    }

                    batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                    batch.UpdateResolvedPoint(dispatchPlan!.ResolvedScreenPoint);
                    batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDoubleClickFirstTap);
                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow);
                    await Task.Delay(effectiveExecutionOptions.DoubleClickDelay, timeProvider, cancellationToken).ConfigureAwait(false);

                    if (!TryDispatchClickWithinBoundary(
                            batch,
                            targetSecurityProbeCache,
                            dispatchPlan,
                            InputDispatchPlanRefreshPolicy.RequireStablePoint,
                            InputButtonValues.Left,
                            InputIrreversiblePhase.AfterDoubleClickSecondTap,
                            cancellationToken,
                            out shortCircuitResult,
                            out liveTargetWindow,
                            out dispatchPlan,
                            out failureCode,
                            out reason,
                            out failureStage))
                    {
                        return MaterializeFactual(
                            shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                                failureCode!,
                                reason!,
                                liveTargetWindow?.Hwnd ?? targetWindow.Hwnd),
                            failureStage);
                    }

                    batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                    batch.UpdateResolvedPoint(dispatchPlan!.ResolvedScreenPoint);
                    batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterDoubleClickSecondTap);
                    if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                    {
                        return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                    }

                    batch.UpdateExpectedTarget(liveTargetWindow);
                    batch.CompleteCurrentActionSuccess();
                    continue;
                }

                if (!TryDispatchClickWithinBoundary(
                        batch,
                        targetSecurityProbeCache,
                        dispatchPlan!,
                        InputDispatchPlanRefreshPolicy.AllowRefreshedPoint,
                        button,
                        InputIrreversiblePhase.AfterClickTap,
                        cancellationToken,
                        out shortCircuitResult,
                        out liveTargetWindow,
                        out dispatchPlan,
                        out failureCode,
                        out reason,
                        out string? clickFailureStage))
                {
                    return MaterializeFactual(
                        shortCircuitResult ?? batch.MaterializeCurrentActionFailure(
                            failureCode!,
                            reason!,
                            liveTargetWindow?.Hwnd ?? targetWindow.Hwnd),
                        clickFailureStage);
                }

                batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);
                batch.UpdateResolvedPoint(dispatchPlan!.ResolvedScreenPoint);
                batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterClickTap);
                if (batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out cancellationResult))
                {
                    return MaterializeFactual(cancellationResult, InputFailureStageValues.CancellationAfterCommittedSideEffect);
                }

                batch.UpdateExpectedTarget(liveTargetWindow);
                batch.CompleteCurrentActionSuccess();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && batch.CommittedSideEffectContext is not null)
        {
            return MaterializeFactual(
                batch.MaterializeExceptionCancellation(cancellationToken),
                InputFailureStageValues.CancellationAfterCommittedSideEffect);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (batch.HasCommittedSideEffectForCurrentAction)
        {
            throw new InputExecutionFailureException(
                MaterializeFactual(
                    batch.MaterializeUnexpectedFailureAfterCommittedSideEffect(),
                    InputFailureStageValues.RuntimeUnhandledAfterCommittedSideEffect,
                    exception),
                exception);
        }
        catch (Exception exception) when (batch.CompletedActionCount > 0)
        {
            throw new InputExecutionFailureException(
                MaterializeFactual(
                    batch.MaterializeUnexpectedFailureAfterCompletedActions(),
                    InputFailureStageValues.RuntimeUnhandledAfterCompletedActions,
                    exception),
                exception);
        }

        if (batch.TryMaterializeCancellationAfterBatchCompleted(cancellationToken, out InputResult? finalCancellationResult))
        {
            return MaterializeFactual(finalCancellationResult, InputFailureStageValues.CancellationAfterBatchCompleted);
        }

        return Materialize(batch.CreateFinalVerifyNeededResult());
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
        InputTargetSecurityProbeCache targetSecurityProbeCache,
        InputPointerDispatchPlan dispatchPlan,
        InputDispatchPlanRefreshPolicy refreshPolicy,
        string button,
        InputIrreversiblePhase partialDispatchCommittedPhase,
        CancellationToken cancellationToken,
        out InputResult? shortCircuitResult,
        [NotNullWhen(true)] out WindowDescriptor? liveTargetWindow,
        out InputPointerDispatchPlan? validatedDispatchPlan,
        out string? failureCode,
        out string? reason,
        out string? failureStage)
    {
        shortCircuitResult = null;
        failureStage = null;

        if (!TryResolveAdmissibleTarget(
                batch,
                targetSecurityProbeCache,
                dispatchPlan,
                out liveTargetWindow,
                out validatedDispatchPlan,
                out failureCode,
                out reason))
        {
            failureStage = InputFailureStagePolicy.ResolveFailureStage(failureCode);
            return false;
        }

        batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);

        InputDispatchPlanBoundaryResult acceptance = AcceptValidatedDispatchPlan(
                batch,
                liveTargetWindow,
                dispatchPlan,
                validatedDispatchPlan!,
                refreshPolicy,
                moveCursorWhenRefreshed: true,
                cancellationToken);
        if (!acceptance.IsSuccess)
        {
            shortCircuitResult = acceptance.ShortCircuitResult;
            liveTargetWindow = acceptance.LiveTargetWindow;
            validatedDispatchPlan = acceptance.DispatchPlan;
            failureCode = acceptance.FailureCode;
            reason = acceptance.Reason;
            failureStage = acceptance.FailureStage;
            return false;
        }

        liveTargetWindow = acceptance.LiveTargetWindow!;
        validatedDispatchPlan = acceptance.DispatchPlan;

        if (!batch.TryEnterDispatchBoundary(cancellationToken, out shortCircuitResult))
        {
            failureCode = shortCircuitResult!.FailureCode;
            reason = shortCircuitResult.Reason;
            failureStage = InputFailureStageValues.CancellationAfterCommittedSideEffect;
            return false;
        }

        InputClickDispatchResult dispatchResult = platform.DispatchClick(
            new InputClickDispatchContext(
                validatedDispatchPlan!.ResolvedScreenPoint,
                button,
                liveTargetWindow));
        if (!dispatchResult.Success)
        {
            if (dispatchResult.CommittedSideEffects)
            {
                batch.UpdateResolvedPoint(validatedDispatchPlan.ResolvedScreenPoint);
                batch.RecordCommittedSideEffect(partialDispatchCommittedPhase);
            }

            failureCode = dispatchResult.FailureCode ?? InputFailureCodeValues.InputDispatchFailed;
            reason = dispatchResult.Reason ?? $"Button dispatch для '{button}' не был подтверждён платформой.";
            failureStage = dispatchResult.FailureStageHint
                ?? InputFailureStagePolicy.MapClickDispatchFailureStage(dispatchResult.OutcomeKind)
                ?? InputFailureStagePolicy.ResolveFailureStage(failureCode);
            return false;
        }

        failureCode = null;
        reason = null;
        failureStage = null;
        return true;
    }

    private bool TryPrepareDispatchPlan(
        InputBatchExecutionState batch,
        InputTargetSecurityProbeCache targetSecurityProbeCache,
        InputPointerDispatchPlan dispatchPlan,
        InputDispatchPlanRefreshPolicy refreshPolicy,
        CancellationToken cancellationToken,
        out InputResult? shortCircuitResult,
        [NotNullWhen(true)] out WindowDescriptor? liveTargetWindow,
        out InputPointerDispatchPlan? preparedDispatchPlan,
        out string? failureCode,
        out string? reason,
        out string? failureStage)
    {
        shortCircuitResult = null;
        failureStage = null;

        if (!TryResolveAdmissibleTarget(
                batch,
                targetSecurityProbeCache,
                dispatchPlan,
                out liveTargetWindow,
                out InputPointerDispatchPlan? validatedDispatchPlan,
                out failureCode,
                out reason))
        {
            preparedDispatchPlan = null;
            failureStage = InputFailureStagePolicy.ResolveFailureStage(failureCode);
            return false;
        }

        batch.UpdateTargetHwnd(liveTargetWindow!.Hwnd);

        InputDispatchPlanBoundaryResult acceptance = AcceptValidatedDispatchPlan(
                batch,
                liveTargetWindow,
                dispatchPlan,
                validatedDispatchPlan!,
                refreshPolicy,
                moveCursorWhenRefreshed: false,
                cancellationToken);
        if (!acceptance.IsSuccess)
        {
            shortCircuitResult = acceptance.ShortCircuitResult;
            liveTargetWindow = acceptance.LiveTargetWindow;
            preparedDispatchPlan = acceptance.DispatchPlan;
            failureCode = acceptance.FailureCode;
            reason = acceptance.Reason;
            failureStage = acceptance.FailureStage;
            return false;
        }

        liveTargetWindow = acceptance.LiveTargetWindow!;
        preparedDispatchPlan = acceptance.DispatchPlan;
        failureStage = null;

        return true;
    }

    private InputDispatchPlanBoundaryResult AcceptValidatedDispatchPlan(
        InputBatchExecutionState batch,
        WindowDescriptor admittedTargetWindow,
        InputPointerDispatchPlan originalDispatchPlan,
        InputPointerDispatchPlan refreshedDispatchPlan,
        InputDispatchPlanRefreshPolicy refreshPolicy,
        bool moveCursorWhenRefreshed,
        CancellationToken cancellationToken)
    {
        if (!InputActionSemantics.SamePoint(originalDispatchPlan.ResolvedScreenPoint, refreshedDispatchPlan.ResolvedScreenPoint))
        {
            if (refreshPolicy == InputDispatchPlanRefreshPolicy.RequireStablePoint)
            {
                return InputDispatchPlanBoundaryResult.Failure(
                    admittedTargetWindow,
                    dispatchPlan: null,
                    failureCode: InputFailureCodeValues.CaptureReferenceStale,
                    reason: "Gesture требует сохранить одну и ту же resolved screen point; boundary refresh потребовал бы retarget.",
                    failureStage: InputFailureStageValues.CoordinateMapping);
            }

            if (moveCursorWhenRefreshed
                && batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out InputResult? postMoveCancellationResult))
            {
                return InputDispatchPlanBoundaryResult.Failure(
                    admittedTargetWindow,
                    dispatchPlan: null,
                    failureCode: postMoveCancellationResult.FailureCode,
                    reason: postMoveCancellationResult.Reason,
                    failureStage: InputFailureStageValues.CancellationAfterCommittedSideEffect,
                    shortCircuitResult: postMoveCancellationResult);
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

                    return InputDispatchPlanBoundaryResult.Failure(
                        admittedTargetWindow,
                        dispatchPlan: null,
                        failureCode: refreshedMoveResult.FailureCode,
                        reason: refreshedMoveResult.Reason,
                        failureStage: InputFailureStageValues.CursorMove);
                }
            }

            if (moveCursorWhenRefreshed)
            {
                batch.RecordCommittedSideEffect(InputIrreversiblePhase.AfterMove);
            }

            if (moveCursorWhenRefreshed
                && batch.TryMaterializeCancellationAfterCommittedSideEffect(cancellationToken, out InputResult? shortCircuitResult))
            {
                return InputDispatchPlanBoundaryResult.Failure(
                    admittedTargetWindow,
                    dispatchPlan: null,
                    failureCode: shortCircuitResult.FailureCode,
                    reason: shortCircuitResult.Reason,
                    failureStage: InputFailureStageValues.CancellationAfterCommittedSideEffect,
                    shortCircuitResult: shortCircuitResult);
            }
        }

        return InputDispatchPlanBoundaryResult.Success(admittedTargetWindow, refreshedDispatchPlan);
    }

    private bool TryResolveAdmissibleTarget(
        InputBatchExecutionState batch,
        InputTargetSecurityProbeCache targetSecurityProbeCache,
        InputPointerDispatchPlan? dispatchPlan,
        [NotNullWhen(true)] out WindowDescriptor? liveTargetWindow,
        out InputPointerDispatchPlan? validatedDispatchPlan,
        out string? failureCode,
        out string? reason)
    {
        LiveWindowIdentityResolution targetResolution = windowTargetResolver.ResolveLiveWindowByIdentity(batch.ExpectedTargetWindow);
        liveTargetWindow = null;
        validatedDispatchPlan = dispatchPlan;
        if (!targetResolution.IsResolved)
        {
            failureCode = InputTargetFailurePolicy.MapStaleTargetFailureCode(batch.TargetSource);
            reason = InputTargetFailurePolicy.CreateTargetFailureReason(failureCode);
            validatedDispatchPlan = null;
            return false;
        }

        liveTargetWindow = targetResolution.Window!;
        if (dispatchPlan is not null
            && !InputCoordinateMapper.TryValidateDispatchPlan(dispatchPlan, liveTargetWindow, out validatedDispatchPlan, out failureCode, out reason))
        {
            return false;
        }

        InputTargetSecurityInfo targetSecurity = targetSecurityProbeCache.Probe(liveTargetWindow);
        InputTargetPreflightResult preflight = InputTargetPreflightPolicy.Evaluate(
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

    private static bool TryResolveSupportedActionTypes(
        string? executionProfile,
        out string[] supportedActionTypes,
        out string? failureCode,
        out string? reason)
    {
        if (string.Equals(executionProfile, InputExecutionProfileValues.ClickFirstPublic, StringComparison.Ordinal))
        {
            supportedActionTypes = ClickFirstSupportedActionTypes;
            failureCode = null;
            reason = null;
            return true;
        }

        if (string.Equals(executionProfile, InputExecutionProfileValues.ComputerUseCore, StringComparison.Ordinal))
        {
            supportedActionTypes = ComputerUseCoreSupportedActionTypes;
            failureCode = null;
            reason = null;
            return true;
        }

        supportedActionTypes = Array.Empty<string>();
        failureCode = InputFailureCodeValues.InvalidRequest;
        reason = string.IsNullOrWhiteSpace(executionProfile)
            ? "Input execution profile не указан."
            : $"Runtime не поддерживает input execution profile '{executionProfile}'.";
        return false;
    }

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

    private readonly record struct InputDispatchPlanBoundaryResult(
        bool IsSuccess,
        InputResult? ShortCircuitResult,
        WindowDescriptor? LiveTargetWindow,
        InputPointerDispatchPlan? DispatchPlan,
        string? FailureCode,
        string? Reason,
        string? FailureStage)
    {
        public static InputDispatchPlanBoundaryResult Success(
            WindowDescriptor liveTargetWindow,
            InputPointerDispatchPlan dispatchPlan) =>
            new(
                IsSuccess: true,
                ShortCircuitResult: null,
                LiveTargetWindow: liveTargetWindow,
                DispatchPlan: dispatchPlan,
                FailureCode: null,
                Reason: null,
                FailureStage: null);

        public static InputDispatchPlanBoundaryResult Failure(
            WindowDescriptor liveTargetWindow,
            InputPointerDispatchPlan? dispatchPlan,
            string? failureCode,
            string? reason,
            string? failureStage,
            InputResult? shortCircuitResult = null) =>
            new(
                IsSuccess: false,
                ShortCircuitResult: shortCircuitResult,
                LiveTargetWindow: liveTargetWindow,
                DispatchPlan: dispatchPlan,
                FailureCode: failureCode,
                Reason: reason,
                FailureStage: failureStage);
    }

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
