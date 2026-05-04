// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinPressKeyHandler(
    ComputerUseWinActionRequestExecutor actionRequestExecutor,
    ComputerUseWinPressKeyExecutionCoordinator pressKeyExecutionCoordinator)
{
    public Task<CallToolResult> ExecuteAsync(
        AuditInvocationScope invocation,
        ComputerUseWinPressKeyRequest request,
        CancellationToken cancellationToken) =>
        actionRequestExecutor.ExecuteAsync(
            invocation,
            ToolNames.ComputerUseWinPressKey,
            request.StateToken,
            elementIndex: null,
            ComputerUseWinStoredStateValidationMode.SemanticElementAction,
            (resolvedState, ct) => pressKeyExecutionCoordinator.ExecuteAsync(resolvedState, request, ct),
            (resolvedState, outcome) => CreateObservabilityContext(resolvedState, request, outcome),
            cancellationToken,
            observeAfter: request.ObserveAfter);

    private static ComputerUseWinActionObservabilityContext CreateObservabilityContext(
        ComputerUseWinStoredState resolvedState,
        ComputerUseWinPressKeyRequest request,
        ComputerUseWinActionExecutionOutcome outcome)
    {
        bool requiresConfirmation = outcome.ConfirmationRequired;
        bool isDangerous = string.Equals(outcome.RiskClass, "dangerous_key", StringComparison.Ordinal);
        bool parsed = ComputerUseWinPressKeyContract.TryParse(request.Key, out ComputerUseWinPressKeyLiteral? literal, out _);
        string layoutResolutionStatus = outcome.Input?.FailureCode switch
        {
            InputFailureCodeValues.UnsupportedKey => "unsupported",
            _ when parsed && literal!.BaseKey.Length == 1 => "invariant_virtual_key",
            _ when parsed => "named_virtual_key",
            _ => "unsupported",
        };

        return new(
            ActionName: ToolNames.ComputerUseWinPressKey,
            RuntimeState: "observed",
            AppId: resolvedState.Session.AppId,
            WindowIdPresent: !string.IsNullOrWhiteSpace(resolvedState.Session.WindowId),
            StateTokenPresent: !string.IsNullOrWhiteSpace(request.StateToken),
            TargetMode: "window",
            ElementIndexPresent: false,
            CoordinateSpace: null,
            CaptureReferencePresent: false,
            ConfirmationRequired: requiresConfirmation,
            Confirmed: request.Confirm,
            RiskClass: outcome.RiskClass,
            DispatchPath: outcome.DispatchPath,
            KeyCategory: parsed ? literal!.KeyCategory : null,
            RepeatCount: request.Repeat ?? 1,
            DangerousCombo: isDangerous,
            LayoutResolutionStatus: layoutResolutionStatus,
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
