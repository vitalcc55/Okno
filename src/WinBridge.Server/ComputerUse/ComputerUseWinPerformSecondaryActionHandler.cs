using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinPerformSecondaryActionHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinPerformSecondaryActionExecutionCoordinator executionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinPerformSecondaryActionRequest request,
        CancellationToken cancellationToken) =>
        actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinPerformSecondaryAction,
            request.StateToken,
            request.ElementIndex,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            (resolvedState, ct) => executionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken);

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinPerformSecondaryActionRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        string? semanticActionKind = ResolveSemanticActionKind(outcome.DispatchPath);

        return new(
            ActionName: ToolNames.ComputerUseWinPerformSecondaryAction,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: "element_index",
            ElementIndexPresent: request.ElementIndex is not null,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: outcome.ConfirmationRequired,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
            FallbackUsed: false,
            SemanticActionKind: semanticActionKind,
            ContextMenuPathUsed: false,
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

    internal static string? ResolveSemanticActionKind(string? dispatchPath) =>
        dispatchPath switch
        {
            "uia_toggle" or "uia_toggle_pattern" => UiaSecondaryActionKindValues.Toggle,
            "uia_expand_collapse" or "uia_expand_collapse_pattern" => UiaSecondaryActionKindValues.ExpandCollapse,
            _ => null,
        };
}
