// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinExecutionFactsPublicationPolicy
{
    public static bool CanPublish(
        ComputerUseWinActionObservabilityContext? context,
        bool factualExecutionObserved) =>
        factualExecutionObserved
        && context is not null
        && !string.IsNullOrWhiteSpace(context.DispatchPath);
}
