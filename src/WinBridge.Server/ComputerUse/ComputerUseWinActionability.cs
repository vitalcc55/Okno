using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinActionability
{
    public static bool IsClickActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.BoundingRectangle is not null
            && !node.IsOffscreen
            && node.IsEnabled;
    }

    public static bool IsClickActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Bounds is not null
            && element.Actions.Contains(ToolNames.ComputerUseWinClick, StringComparer.Ordinal);
    }

    public static bool HasSemanticFallbackSignal(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return !string.IsNullOrWhiteSpace(element.AutomationId);
    }
}
