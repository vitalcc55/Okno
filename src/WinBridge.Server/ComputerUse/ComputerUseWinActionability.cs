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

    public static bool IsSetValueActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && SupportsWritableValue(node);
    }

    public static bool IsSetValueActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Actions.Contains(ToolNames.ComputerUseWinSetValue, StringComparer.Ordinal);
    }

    public static bool IsScrollActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && node.Patterns.Contains("scroll", StringComparer.Ordinal);
    }

    public static bool IsScrollActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Actions.Contains(ToolNames.ComputerUseWinScroll, StringComparer.Ordinal);
    }

    public static bool IsDragEndpointActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.BoundingRectangle is not null
            && node.IsEnabled
            && !node.IsOffscreen;
    }

    public static bool IsDragEndpointActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Bounds is not null
            && element.Actions.Contains(ToolNames.ComputerUseWinDrag, StringComparer.Ordinal);
    }

    public static bool IsPerformSecondaryActionActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && ComputerUseWinSecondaryActionResolver.TryResolveActionKind(node.Patterns, out _);
    }

    public static bool IsPerformSecondaryActionActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Actions.Contains(ToolNames.ComputerUseWinPerformSecondaryAction, StringComparer.Ordinal)
            && ComputerUseWinSecondaryActionResolver.TryResolveActionKind(element.Patterns, out _);
    }

    public static bool IsTypeTextActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && node.HasKeyboardFocus
            && IsEditableTextControl(node.ControlType)
            && SupportsWritableValue(node);
    }

    public static bool IsTypeTextActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.HasKeyboardFocus
            && IsEditableTextControl(element.ControlType)
            && element.Actions.Contains(ToolNames.ComputerUseWinTypeText, StringComparer.Ordinal);
    }

    public static bool HasSemanticFallbackSignal(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return !string.IsNullOrWhiteSpace(element.AutomationId);
    }

    private static bool IsEditableTextControl(string controlType) =>
        string.Equals(controlType, "edit", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsWritableValue(UiaElementSnapshot node) =>
        node.IsReadOnly is false
        && (node.Patterns.Contains("value", StringComparer.Ordinal)
            || node.Patterns.Contains("range_value", StringComparer.Ordinal));
}
