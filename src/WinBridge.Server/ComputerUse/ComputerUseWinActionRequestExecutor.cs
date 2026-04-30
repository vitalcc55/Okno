using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinActionRequestExecutor
{
    private readonly ComputerUseWinStoredStateResolver storedStateResolver;
    private readonly ComputerUseWinAppStateObserver? appStateObserver;
    private readonly ComputerUseWinStateStore? stateStore;
    private readonly ISessionManager? sessionManager;

    public ComputerUseWinActionRequestExecutor(ComputerUseWinStoredStateResolver storedStateResolver)
    {
        ArgumentNullException.ThrowIfNull(storedStateResolver);

        this.storedStateResolver = storedStateResolver;
    }

    public ComputerUseWinActionRequestExecutor(
        ComputerUseWinStoredStateResolver storedStateResolver,
        ComputerUseWinAppStateObserver appStateObserver,
        ComputerUseWinStateStore stateStore,
        ISessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(storedStateResolver);
        ArgumentNullException.ThrowIfNull(appStateObserver);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(sessionManager);

        this.storedStateResolver = storedStateResolver;
        this.appStateObserver = appStateObserver;
        this.stateStore = stateStore;
        this.sessionManager = sessionManager;
    }

    public async Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        string toolName,
        string? stateToken,
        int? elementIndex,
        ComputerUseWinStoredStateValidationMode validationMode,
        Func<ComputerUseWinStoredState, CancellationToken, Task<ComputerUseWinActionExecutionOutcome>> execute,
        Func<ComputerUseWinStoredState, ComputerUseWinActionExecutionOutcome, ComputerUseWinActionObservabilityContext>? createObservabilityContext,
        CancellationToken cancellationToken,
        bool observeAfter = false,
        bool preDispatchStateMutationPossible = true)
    {
        ComputerUseWinActionObservabilityContext requestEnvelopeObservabilityContext = CreateRequestObservabilityContext(
            toolName,
            stateToken,
            elementIndex,
            observeAfter);
        if (!storedStateResolver.TryResolve(
                stateToken,
                invocation,
                toolName,
                validationMode,
                out ComputerUseWinActionReadyState? state,
                out CallToolResult? failureResult,
                requestEnvelopeObservabilityContext))
        {
            return failureResult!;
        }

        ComputerUseWinStoredState resolvedState = state!.StoredState;

        try
        {
            ComputerUseWinActionExecutionOutcome outcome = await execute(resolvedState, cancellationToken).ConfigureAwait(false);
            ComputerUseWinActionObservabilityContext? observabilityContext = createObservabilityContext?.Invoke(resolvedState, outcome);
            ComputerUseWinActionObservabilityContext? requestObservabilityContext = MarkObserveAfterRequest(
                observabilityContext,
                observeAfter);

            if (outcome.IsApprovalRequired)
            {
                return ComputerUseWinToolResultFactory.CreateActionApprovalRequired(
                    invocation,
                    toolName,
                    resolvedState.Session.Hwnd,
                    elementIndex,
                    outcome.ApprovalReason!,
                    outcome.Phase,
                    requestObservabilityContext);
            }

            if (!outcome.IsSuccess)
            {
                return ComputerUseWinToolResultFactory.CreateActionFailure(
                    invocation,
                    toolName,
                    outcome.FailureDetails!,
                    resolvedState.Session.Hwnd,
                    elementIndex,
                    outcome.Phase,
                    requestObservabilityContext);
            }

            ComputerUseWinActionSuccessorObservation? successorObservation = await TryObserveSuccessorStateAsync(
                observeAfter,
                resolvedState,
                outcome,
                cancellationToken).ConfigureAwait(false);

            return ComputerUseWinToolResultFactory.CreateActionToolResult(
                invocation,
                toolName,
                resolvedState.Session.Hwnd,
                elementIndex,
                outcome.Input!,
                EnrichObservabilityContext(
                    requestObservabilityContext,
                    observeAfter,
                    successorObservation),
                successorObservation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InputExecutionFailureException exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                toolName,
                resolvedState.Session.Hwnd,
                elementIndex,
                exception.InnerException ?? exception,
                exception.Result,
                observabilityContext: CreateFallbackObservabilityContext(toolName, resolvedState, elementIndex, observeAfter));
        }
        catch (Exception exception)
        {
            return ComputerUseWinActionFinalizer.FinalizeUnexpectedFailure(
                invocation,
                toolName,
                resolvedState.Session.Hwnd,
                elementIndex,
                exception,
                preDispatchStateMutationPossible: preDispatchStateMutationPossible,
                observabilityContext: CreateFallbackObservabilityContext(toolName, resolvedState, elementIndex, observeAfter));
        }
    }

    private async Task<ComputerUseWinActionSuccessorObservation?> TryObserveSuccessorStateAsync(
        bool observeAfter,
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinActionExecutionOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (!observeAfter || !IsCommittedInput(outcome.Input))
        {
            return null;
        }

        if (appStateObserver is null || stateStore is null || sessionManager is null)
        {
            return ComputerUseWinActionSuccessorObservation.Failed(
                new ComputerUseWinActionSuccessorStateFailure(
                    ComputerUseWinFailureCodeValues.UnexpectedInternalFailure,
                    "Computer Use for Windows не смог выполнить observeAfter: shared observation dependencies недоступны."));
        }

        try
        {
            WindowDescriptor candidateWindow = outcome.SuccessorObservationWindow ?? resolvedState.Window;
            if (!storedStateResolver.TryResolveSuccessorObservationWindow(
                    candidateWindow,
                    out WindowDescriptor? selectedWindow,
                    out ComputerUseWinFailureDetails? targetFailure))
            {
                ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(
                    targetFailure!.FailureCode,
                    targetFailure.Reason);
                return ComputerUseWinActionSuccessorObservation.Failed(
                    new ComputerUseWinActionSuccessorStateFailure(
                        failure.FailureCode ?? ComputerUseWinFailureCodeValues.StaleState,
                        failure.Reason ?? "Computer Use for Windows не смог подтвердить post-action target для observeAfter."));
            }

            ComputerUseWinAppStateObservationOutcome observation = await appStateObserver.ObserveAsync(
                selectedWindow!,
                resolvedState.Session.AppId,
                windowId: null,
                resolvedState.Observation.RequestedMaxNodes,
                warnings: [],
                cancellationToken).ConfigureAwait(false);
            if (!observation.IsSuccess)
            {
                ComputerUseWinFailureTranslation failure = ComputerUseWinFailureCodeMapper.ToPublicFailure(
                    observation.FailureCode,
                    observation.Reason);
                return ComputerUseWinActionSuccessorObservation.Failed(
                    new ComputerUseWinActionSuccessorStateFailure(
                        failure.FailureCode ?? ComputerUseWinFailureCodeValues.ObservationFailed,
                        failure.Reason ?? "Computer Use for Windows не смог materialize successorState после committed action."));
            }

            ComputerUseWinMaterializedAppState materializedState = ComputerUseWinGetAppStateFinalizer.CommitPreparedState(
                observation.PreparedState!,
                stateStore,
                sessionManager,
                selectedWindow!);
            return ComputerUseWinActionSuccessorObservation.Success(materializedState);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ComputerUseWinActionSuccessorObservation.Failed(
                new ComputerUseWinActionSuccessorStateFailure(
                    ComputerUseWinFailureCodeValues.UnexpectedInternalFailure,
                    "Computer Use for Windows не смог materialize successorState после committed action."));
        }
    }

    private static bool IsCommittedInput(InputResult? input) =>
        input is not null
        && (string.Equals(input.Status, InputStatusValues.Done, StringComparison.Ordinal)
            || string.Equals(input.Status, InputStatusValues.VerifyNeeded, StringComparison.Ordinal));

    private static ComputerUseWinActionObservabilityContext? MarkObserveAfterRequest(
        ComputerUseWinActionObservabilityContext? context,
        bool observeAfter)
    {
        if (context is null)
        {
            return null;
        }

        return context with
        {
            ObserveAfterRequested = observeAfter,
        };
    }

    private static ComputerUseWinActionObservabilityContext CreateFallbackObservabilityContext(
        string toolName,
        ComputerUseWinStoredState resolvedState,
        int? elementIndex,
        bool observeAfter) =>
        new(
            ActionName: toolName,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: true,
            TargetMode: elementIndex is null ? "unknown" : "element_index",
            ElementIndexPresent: elementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: resolvedState.CaptureReference is not null,
            ConfirmationRequired: false,
            Confirmed: false,
            RiskClass: null,
            DispatchPath: null,
            ObserveAfterRequested: observeAfter);

    private static ComputerUseWinActionObservabilityContext CreateRequestObservabilityContext(
        string toolName,
        string? stateToken,
        int? elementIndex,
        bool observeAfter) =>
        new(
            ActionName: toolName,
            RuntimeState: "unresolved",
            AppId: "unknown",
            WindowIdPresent: false,
            StateTokenPresent: !string.IsNullOrWhiteSpace(stateToken),
            TargetMode: elementIndex is null ? "unknown" : "element_index",
            ElementIndexPresent: elementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: false,
            Confirmed: false,
            RiskClass: null,
            DispatchPath: null,
            ObserveAfterRequested: observeAfter);

    private static ComputerUseWinActionObservabilityContext? EnrichObservabilityContext(
        ComputerUseWinActionObservabilityContext? context,
        bool observeAfter,
        ComputerUseWinActionSuccessorObservation? successorObservation)
    {
        if (context is null)
        {
            return null;
        }

        return context with
        {
            ObserveAfterRequested = observeAfter,
            SuccessorStateAvailable = successorObservation?.SuccessorState is not null,
            SuccessorStateFailureCode = successorObservation?.Failure?.FailureCode,
        };
    }
}
