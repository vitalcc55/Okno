// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Input;

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
            DetermineValidationMode(request),
            (resolvedState, ct) => typeTextExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken,
            observeAfter: request.ObserveAfter);

    private static ComputerUseWinStoredStateValidationMode DetermineValidationMode(ComputerUseWinTypeTextRequest request)
    {
        if (request.Point is null)
        {
            return ComputerUseWinStoredStateValidationMode.SemanticElementAction;
        }

        return ComputerUseWinStoredStateValidationMode.CoordinateCapturePixelsAction;
    }

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
                ? request.Point is not null
                    ? "coordinate_confirmed_fallback"
                    : request.ElementIndex is null ? "focused_fallback" : "element_focused_fallback"
                : request.ElementIndex is null ? "focused_editable" : "element_index",
            ElementIndexPresent: request.ElementIndex is not null,
            CoordinateSpace: parsed ? payload!.CoordinateSpace : request.CoordinateSpace,
            CaptureReferencePresent: request.Point is not null && resolvedState.CaptureReference is not null,
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
