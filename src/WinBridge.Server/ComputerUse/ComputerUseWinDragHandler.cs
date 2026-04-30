using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinDragHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinDragExecutionCoordinator dragExecutionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinDragRequest request,
        CancellationToken cancellationToken) =>
        actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinDrag,
            request.StateToken,
            request.FromElementIndex ?? request.ToElementIndex,
            DetermineValidationMode(request),
            (resolvedState, ct) => dragExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken,
            observeAfter: request.ObserveAfter);

    private static ComputerUseWinStoredStateValidationMode DetermineValidationMode(ComputerUseWinDragRequest request)
    {
        if (request.FromPoint is null && request.ToPoint is null)
        {
            return ComputerUseWinStoredStateValidationMode.SemanticElementAction;
        }

        if (!ComputerUseWinDragContract.TryParse(request, out ComputerUseWinDragPayload? payload, out _))
        {
            return ComputerUseWinStoredStateValidationMode.CoordinateScreenAction;
        }

        return string.Equals(payload!.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
            ? ComputerUseWinStoredStateValidationMode.CoordinateCapturePixelsAction
            : ComputerUseWinStoredStateValidationMode.CoordinateScreenAction;
    }

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinDragRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        bool parsed = ComputerUseWinDragContract.TryParse(request, out ComputerUseWinDragPayload? payload, out _);

        return new(
            ActionName: ToolNames.ComputerUseWinDrag,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: parsed
                ? $"{payload!.Source.Mode}_to_{payload.Destination.Mode}"
                : "drag_path",
            ElementIndexPresent: request.FromElementIndex is not null || request.ToElementIndex is not null,
            CoordinateSpace: parsed ? payload!.CoordinateSpace : request.CoordinateSpace,
            CaptureReferencePresent: string.Equals(parsed ? payload!.CoordinateSpace : request.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal),
            ConfirmationRequired: outcome.ConfirmationRequired,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
            SourceMode: parsed ? payload!.Source.Mode : (request.FromPoint is null ? "element_index" : "point"),
            DestinationMode: parsed ? payload!.Destination.Mode : (request.ToPoint is null ? "element_index" : "point"),
            PathPointCountBucket: parsed ? payload!.PathPointCountBucket : "unknown",
            CoordinateFallbackUsed: parsed ? payload!.UsesCoordinateEndpoint : request.FromPoint is not null || request.ToPoint is not null,
            ChildArtifactPath: null,
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
