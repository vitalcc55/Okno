// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinPerformSecondaryActionContract
{
    public static string? ValidateRequest(ComputerUseWinPerformSecondaryActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StateToken))
        {
            return "Параметр stateToken обязателен для perform_secondary_action.";
        }

        if (request.ElementIndex is null or < 1)
        {
            return "Параметр elementIndex для perform_secondary_action должен быть >= 1.";
        }

        return null;
    }
}
