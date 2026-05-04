// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinStoredStateResolver(
    ComputerUseWinStateStore stateStore,
    IWindowManager windowManager)
{
    public bool TryResolve(
        string? stateToken,
        AuditInvocationScope invocation,
        string toolName,
        ComputerUseWinStoredStateValidationMode validationMode,
        out ComputerUseWinActionReadyState? state,
        out CallToolResult? failureResult,
        ComputerUseWinActionObservabilityContext? requestObservabilityContext = null)
    {
        ComputerUseWinRuntimeState staleState = ComputerUseWinRuntimeStateModel.Stale();
        if (string.IsNullOrWhiteSpace(stateToken))
        {
            state = null;
            failureResult = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                toolName,
                ComputerUseWinFailureCodeValues.StateRequired,
                "Сначала вызови get_app_state и передай stateToken.",
                observabilityContext: requestObservabilityContext);
            return false;
        }

        if (!stateStore.TryGet(stateToken, out ComputerUseWinStoredState? storedState) || storedState is null)
        {
            state = null;
            if (ComputerUseWinRuntimeStateModel.CanExecuteAction(staleState))
            {
                throw new InvalidOperationException("Stale runtime state must not become action-ready.");
            }

            failureResult = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                toolName,
                ComputerUseWinFailureCodeValues.StaleState,
                "stateToken больше не найден; заново вызови get_app_state.",
                observabilityContext: requestObservabilityContext);
            return false;
        }

        bool liveWindowResolved = ComputerUseWinLiveWindowSelector.TrySelectSingle(
            windowManager.ListWindows(),
            item => ComputerUseWinWindowContinuityProof.MatchesObservedState(item, storedState, validationMode),
            out WindowDescriptor? liveWindow,
            out bool ambiguous);
        if (!liveWindowResolved)
        {
            state = null;
            if (ComputerUseWinRuntimeStateModel.CanExecuteAction(staleState))
            {
                throw new InvalidOperationException("Stale runtime state must not become action-ready.");
            }

            failureResult = ComputerUseWinToolResultFactory.CreateActionFailure(
                invocation,
                toolName,
                ambiguous ? ComputerUseWinFailureCodeValues.AmbiguousTarget : ComputerUseWinFailureCodeValues.StaleState,
                ambiguous
                    ? "Computer Use for Windows не смог однозначно сопоставить stateToken с текущим live target."
                    : "Окно из stateToken больше не совпадает с текущим live target.",
                observabilityContext: requestObservabilityContext);
            return false;
        }

        state = ComputerUseWinRuntimeStateModel.CreateActionReadyState(storedState with { Window = liveWindow! });
        failureResult = null;
        return true;
    }

    public bool TryResolveSuccessorObservationWindow(
        WindowDescriptor candidateWindow,
        out WindowDescriptor? liveWindow,
        out ComputerUseWinFailureDetails? failureDetails)
    {
        ArgumentNullException.ThrowIfNull(candidateWindow);

        liveWindow = null;
        failureDetails = null;
        bool resolved = ComputerUseWinLiveWindowSelector.TrySelectSingle(
            windowManager.ListWindows(),
            item => ComputerUseWinWindowContinuityProof.MatchesAttachedSession(item, candidateWindow),
            out liveWindow,
            out bool ambiguous);
        if (ambiguous)
        {
            failureDetails = ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.AmbiguousTarget,
                "Computer Use for Windows не смог однозначно сопоставить post-action target window для observeAfter.");
            return false;
        }

        if (!resolved)
        {
            failureDetails = ComputerUseWinFailureDetails.Expected(
                ComputerUseWinFailureCodeValues.StaleState,
                "Целевое окно после committed action больше не найдено; заново вызови get_app_state.");
            return false;
        }

        return true;
    }
}
