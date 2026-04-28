using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinSetValueHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinSetValueExecutionCoordinator setValueExecutionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinSetValueRequest request,
        CancellationToken cancellationToken) =>
        actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinSetValue,
            request.StateToken,
            request.ElementIndex,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            (resolvedState, ct) => setValueExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken);

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinSetValueRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        bool parsed = ComputerUseWinSetValueContract.TryParse(request, out ComputerUseWinSetValuePayload? payload, out _);

        return new(
            ActionName: ToolNames.ComputerUseWinSetValue,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: "element_index",
            ElementIndexPresent: request.ElementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: false,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
            ValueKind: parsed ? payload!.ValueKind : null,
            ValueLength: parsed ? payload!.ValueLength : null,
            ValueBucket: parsed ? payload!.ValueBucket : null,
            ChildArtifactPath: outcome.Input?.ArtifactPath,
            FailureStage: outcome.Phase switch
            {
                ComputerUseWinActionLifecyclePhase.BeforeActivation => "pre_dispatch_reject",
                ComputerUseWinActionLifecyclePhase.AfterActivationBeforeDispatch => "pre_dispatch_after_activation",
                ComputerUseWinActionLifecyclePhase.AfterRevalidationBeforeDispatch => "after_revalidation_before_dispatch",
                _ => "post_dispatch",
            },
            ExceptionType: null);
    }
}
