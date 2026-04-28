using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinClickHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinClickExecutionCoordinator clickExecutionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinClickRequest request,
        CancellationToken cancellationToken)
        => actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinClick,
            request.StateToken,
            request.ElementIndex,
            DetermineValidationMode(request),
            (resolvedState, ct) => clickExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken);

    private static ComputerUseWinStoredStateValidationMode DetermineValidationMode(ComputerUseWinClickRequest request)
    {
        if (request.ElementIndex is not null)
        {
            return ComputerUseWinStoredStateValidationMode.SemanticElementAction;
        }

        string coordinateSpace = request.CoordinateSpace is null
            ? InputCoordinateSpaceValues.CapturePixels
            : request.CoordinateSpace!;
        return string.Equals(coordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
            ? ComputerUseWinStoredStateValidationMode.CoordinateCapturePixelsAction
            : ComputerUseWinStoredStateValidationMode.CoordinateScreenAction;
    }

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinClickRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        string targetMode = request.ElementIndex is not null ? "element_index" : "point";
        string coordinateSpace = request.ElementIndex is not null
            ? InputCoordinateSpaceValues.Screen
            : request.CoordinateSpace ?? InputCoordinateSpaceValues.CapturePixels;

        return new(
            ActionName: ToolNames.ComputerUseWinClick,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: targetMode,
            ElementIndexPresent: request.ElementIndex is not null,
            CoordinateSpace: coordinateSpace,
            CaptureReferencePresent: resolvedState.CaptureReference is not null,
            ConfirmationRequired: outcome.ConfirmationRequired,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
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
