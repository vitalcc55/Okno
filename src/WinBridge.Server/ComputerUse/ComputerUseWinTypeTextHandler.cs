using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinTypeTextHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinTypeTextExecutionCoordinator typeTextExecutionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinTypeTextRequest request,
        CancellationToken cancellationToken) =>
        actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinTypeText,
            request.StateToken,
            request.ElementIndex,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            (resolvedState, ct) => typeTextExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken,
            observeAfter: request.ObserveAfter);

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinTypeTextRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        bool parsed = ComputerUseWinTypeTextContract.TryParse(request, out ComputerUseWinTypeTextPayload? payload, out _);

        return new(
            ActionName: ToolNames.ComputerUseWinTypeText,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: outcome.FallbackUsed
                ? request.ElementIndex is null ? "focused_fallback" : "element_focused_fallback"
                : request.ElementIndex is null ? "focused_editable" : "element_index",
            ElementIndexPresent: request.ElementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: outcome.ConfirmationRequired,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
            TextLength: parsed ? payload!.TextLength : null,
            TextBucket: parsed ? payload!.TextBucket : null,
            ContainsNewline: parsed ? payload!.ContainsNewline : null,
            WhitespaceOnly: parsed ? payload!.WhitespaceOnly : null,
            FallbackUsed: outcome.FallbackUsed,
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
