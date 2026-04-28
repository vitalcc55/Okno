using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinAccessibilityProjector
{
    public static IReadOnlyDictionary<int, ComputerUseWinStoredElement> Flatten(UiaElementSnapshot? root)
    {
        Dictionary<int, ComputerUseWinStoredElement> result = new();
        if (root is null)
        {
            return result;
        }

        int index = 1;
        Visit(root);
        return result;

        void Visit(UiaElementSnapshot node)
        {
            result[index] = new ComputerUseWinStoredElement(
                index,
                node.ElementId,
                node.Name,
                node.AutomationId,
                node.ControlType,
                node.BoundingRectangle,
                node.HasKeyboardFocus,
                ComputerUseWinAffordanceResolver.Resolve(node),
                node.Patterns);
            index++;

            foreach (UiaElementSnapshot child in node.Children)
            {
                Visit(child);
            }
        }
    }

    public static IReadOnlyList<ComputerUseWinAccessibilityElement> CreatePublicTree(IReadOnlyDictionary<int, ComputerUseWinStoredElement> elements) =>
        elements.Values
            .OrderBy(static item => item.Index)
            .Select(static item => new ComputerUseWinAccessibilityElement(
                item.Index,
                item.ElementId,
                item.Name,
                item.AutomationId,
                item.ControlType,
                item.Bounds,
                item.HasKeyboardFocus,
                item.Actions))
            .ToArray();
}
