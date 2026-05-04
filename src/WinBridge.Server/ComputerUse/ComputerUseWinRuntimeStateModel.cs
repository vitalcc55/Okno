// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Server.ComputerUse;

internal enum ComputerUseWinRuntimeStateKind
{
    Attached,
    Approved,
    Observed,
    Stale,
    Blocked,
}

internal readonly record struct ComputerUseWinRuntimeState(ComputerUseWinRuntimeStateKind Kind)
{
    public bool IsActionReady => Kind == ComputerUseWinRuntimeStateKind.Observed;
}

internal sealed record ComputerUseWinActionReadyState(
    ComputerUseWinStoredState StoredState,
    ComputerUseWinRuntimeState RuntimeState);

internal static class ComputerUseWinRuntimeStateModel
{
    public static ComputerUseWinRuntimeState Attached() => new(ComputerUseWinRuntimeStateKind.Attached);

    public static ComputerUseWinRuntimeState Approved() => new(ComputerUseWinRuntimeStateKind.Approved);

    public static ComputerUseWinRuntimeState Observed() => new(ComputerUseWinRuntimeStateKind.Observed);

    public static ComputerUseWinRuntimeState Stale() => new(ComputerUseWinRuntimeStateKind.Stale);

    public static ComputerUseWinRuntimeState Blocked() => new(ComputerUseWinRuntimeStateKind.Blocked);

    public static bool CanExecuteAction(ComputerUseWinRuntimeState state) => state.IsActionReady;

    public static bool CanPromoteToObserved(ComputerUseWinRuntimeState state, bool hasFreshObservation) =>
        hasFreshObservation
        && state.Kind is ComputerUseWinRuntimeStateKind.Attached or ComputerUseWinRuntimeStateKind.Approved;

    public static ComputerUseWinActionReadyState CreateActionReadyState(ComputerUseWinStoredState storedState)
    {
        ArgumentNullException.ThrowIfNull(storedState);

        ComputerUseWinRuntimeState observedState = Observed();
        if (!CanExecuteAction(observedState))
        {
            throw new InvalidOperationException("Observed runtime state must remain action-ready.");
        }

        return new ComputerUseWinActionReadyState(storedState, observedState);
    }
}
