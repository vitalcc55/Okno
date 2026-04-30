using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinScrollHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinScrollExecutionCoordinator scrollExecutionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinScrollRequest request,
        CancellationToken cancellationToken) =>
        actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinScroll,
            request.StateToken,
            request.ElementIndex,
            DetermineValidationMode(request),
            (resolvedState, ct) => scrollExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken,
            observeAfter: request.ObserveAfter);

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinScrollRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        bool parsed = ComputerUseWinScrollContract.TryParse(request, out ComputerUseWinScrollPayload? payload, out _);
        bool usesPointFallback = parsed && string.Equals(payload!.TargetMode, "point", StringComparison.Ordinal);

        return new(
            ActionName: ToolNames.ComputerUseWinScroll,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: parsed ? payload!.TargetMode : (request.Point is null ? "element_index" : "point"),
            ElementIndexPresent: request.ElementIndex is not null,
            CoordinateSpace: parsed ? payload!.CoordinateSpace : request.CoordinateSpace,
            CaptureReferencePresent: string.Equals(parsed ? payload!.CoordinateSpace : request.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal),
            ConfirmationRequired: outcome.ConfirmationRequired,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
            ScrollDirection: parsed ? payload!.Direction : request.Direction,
            ScrollAmountBucket: parsed ? payload!.DeltaBucket : null,
            ScrollUnit: usesPointFallback ? "wheel_delta" : "page",
            SemanticScrollSupported: usesPointFallback ? false : true,
            FallbackUsed: usesPointFallback,
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

    private static ComputerUseWinStoredStateValidationMode DetermineValidationMode(ComputerUseWinScrollRequest request)
    {
        if (request.Point is null)
        {
            return ComputerUseWinStoredStateValidationMode.SemanticElementAction;
        }

        if (!ComputerUseWinScrollContract.TryParse(request, out ComputerUseWinScrollPayload? payload, out _))
        {
            return ComputerUseWinStoredStateValidationMode.CoordinateScreenAction;
        }

        return string.Equals(payload!.CoordinateSpace, InputCoordinateSpaceValues.CapturePixels, StringComparison.Ordinal)
            ? ComputerUseWinStoredStateValidationMode.CoordinateCapturePixelsAction
            : ComputerUseWinStoredStateValidationMode.CoordinateScreenAction;
    }
}
