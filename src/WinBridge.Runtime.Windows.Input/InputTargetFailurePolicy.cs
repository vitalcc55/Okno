// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputTargetFailurePolicy
{
    public static string MapStaleTargetFailureCode(string? targetSource) =>
        string.Equals(targetSource, InputTargetSourceValues.Attached, StringComparison.Ordinal)
            ? InputFailureCodeValues.StaleAttachedTarget
            : InputFailureCodeValues.StaleExplicitTarget;

    public static string CreateTargetFailureReason(string? failureCode) =>
        failureCode switch
        {
            InputFailureCodeValues.StaleExplicitTarget => "Explicit target больше не совпадает с live window identity.",
            InputFailureCodeValues.StaleAttachedTarget => "Attached target больше не совпадает с live window identity.",
            InputFailureCodeValues.MissingTarget => "windows.input Package B требует explicit или attached target без active fallback.",
            _ => "Runtime не смог разрешить target для windows.input.",
        };
}
