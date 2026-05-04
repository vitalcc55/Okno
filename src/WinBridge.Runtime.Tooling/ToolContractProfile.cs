// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Tooling;

public sealed record ToolContractProfile(
    string Name,
    IReadOnlyList<ToolDescriptor> Implemented,
    IReadOnlyList<ToolDescriptor> Deferred,
    IReadOnlyList<string> ImplementedNames,
    IReadOnlyList<string> SmokeRequiredToolNames,
    IReadOnlyDictionary<string, string> DeferredPhaseMap,
    string Notes);

public static class ToolSurfaceProfileValues
{
    public const string WindowsEngine = "windows-engine";
    public const string ComputerUseWin = "computer-use-win";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            WindowsEngine,
            ComputerUseWin,
        };
}
