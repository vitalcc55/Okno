// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinAffordanceResolver
{
    private static readonly HashSet<string> ImplementedToolNames = ToolContractManifest
        .GetProfile(ToolSurfaceProfileValues.ComputerUseWin)
        .ImplementedNames
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Resolve(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        List<string> actions = [];
        if (ComputerUseWinActionability.IsClickActionable(node)
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinClick))
        {
            actions.Add(ToolNames.ComputerUseWinClick);
        }

        if (ComputerUseWinActionability.IsDragEndpointActionable(node)
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinDrag))
        {
            actions.Add(ToolNames.ComputerUseWinDrag);
        }

        if (ComputerUseWinActionability.IsSetValueActionable(node)
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinSetValue))
        {
            actions.Add(ToolNames.ComputerUseWinSetValue);
        }

        if (ComputerUseWinActionability.IsPerformSecondaryActionActionable(node)
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinPerformSecondaryAction))
        {
            actions.Add(ToolNames.ComputerUseWinPerformSecondaryAction);
        }

        if (ComputerUseWinActionability.IsScrollActionable(node)
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinScroll))
        {
            actions.Add(ToolNames.ComputerUseWinScroll);
        }

        if (ComputerUseWinActionability.IsTypeTextActionable(node)
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinTypeText))
        {
            actions.Add(ToolNames.ComputerUseWinTypeText);
        }

        return actions;
    }
}
