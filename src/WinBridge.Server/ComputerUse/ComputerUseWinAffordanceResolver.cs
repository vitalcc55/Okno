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

        if ((string.Equals(node.ControlType, "edit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.ControlType, "document", StringComparison.OrdinalIgnoreCase))
            && ImplementedToolNames.Contains(ToolNames.ComputerUseWinTypeText))
        {
            actions.Add(ToolNames.ComputerUseWinTypeText);
        }

        return actions;
    }
}
