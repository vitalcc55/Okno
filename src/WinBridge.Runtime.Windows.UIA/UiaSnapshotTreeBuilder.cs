using System.Globalization;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiaSnapshotTreeBuilder
{
    public static UiaSnapshotTreeBuildResult Build(
        IUiaSnapshotNode rootNode,
        int maxDepth,
        int maxNodes,
        CancellationToken cancellationToken)
    {
        BuilderState state = new(maxDepth, maxNodes);
        UiaElementSnapshot root = state.BuildNode(rootNode, parentElementId: null, depth: 0, ordinal: 0, path: "0", cancellationToken);
        return new(root, state.RealizedDepth, state.NodeCount, state.Truncated, state.DepthBoundaryReached, state.NodeBudgetBoundaryReached);
    }

    internal sealed record UiaSnapshotTreeBuildResult(
        UiaElementSnapshot Root,
        int RealizedDepth,
        int NodeCount,
        bool Truncated,
        bool DepthBoundaryReached,
        bool NodeBudgetBoundaryReached);

    private sealed class BuilderState(int maxDepth, int maxNodes)
    {
        public int RealizedDepth { get; private set; }

        public int NodeCount { get; private set; }

        public bool Truncated { get; private set; }

        public bool DepthBoundaryReached { get; private set; }

        public bool NodeBudgetBoundaryReached { get; private set; }

        public UiaElementSnapshot BuildNode(
            IUiaSnapshotNode node,
            string? parentElementId,
            int depth,
            int ordinal,
            string path,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (NodeCount >= maxNodes)
            {
                throw new InvalidOperationException("UIA snapshot builder превысил допустимый node budget.");
            }

            NodeCount++;
            RealizedDepth = Math.Max(RealizedDepth, depth);

            UiaSnapshotNodeData data = node.GetData();
            string elementId = UiaElementIdBuilder.Create(data.RuntimeId, path);
            List<UiaElementSnapshot> children = [];

            if (depth >= maxDepth)
            {
                DepthBoundaryReached = true;
            }
            else
            {
                if (!HasChildBudget())
                {
                    NodeBudgetBoundaryReached = true;
                    return CreateSnapshot(data, elementId, parentElementId, depth, ordinal, children);
                }

                IUiaSnapshotNode? firstChild = node.GetFirstChild();
                if (firstChild is null)
                {
                    return CreateSnapshot(data, elementId, parentElementId, depth, ordinal, children);
                }

                int childOrdinal = 0;
                IUiaSnapshotNode? child = firstChild;
                while (child is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!HasChildBudget())
                    {
                        NodeBudgetBoundaryReached = true;
                        Truncated = true;
                        break;
                    }

                    IUiaSnapshotNode? nextSibling = child.GetNextSibling();
                    string childPath = path + "/" + childOrdinal.ToString(CultureInfo.InvariantCulture);
                    children.Add(BuildNode(child, elementId, depth + 1, childOrdinal, childPath, cancellationToken));
                    childOrdinal++;
                    child = nextSibling;
                }
            }

            return CreateSnapshot(data, elementId, parentElementId, depth, ordinal, children);
        }

        private bool HasChildBudget() => NodeCount < maxNodes;

        private static UiaElementSnapshot CreateSnapshot(
            UiaSnapshotNodeData data,
            string elementId,
            string? parentElementId,
            int depth,
            int ordinal,
            IReadOnlyList<UiaElementSnapshot> children) =>
            new()
            {
                ElementId = elementId,
                ParentElementId = parentElementId,
                Depth = depth,
                Ordinal = ordinal,
                Name = data.Name,
                AutomationId = data.AutomationId,
                ClassName = data.ClassName,
                FrameworkId = data.FrameworkId,
                ControlType = data.ControlType,
                ControlTypeId = data.ControlTypeId,
                LocalizedControlType = data.LocalizedControlType,
                IsControlElement = data.IsControlElement,
                IsContentElement = data.IsContentElement,
                IsEnabled = data.IsEnabled,
                IsOffscreen = data.IsOffscreen,
                HasKeyboardFocus = data.HasKeyboardFocus,
                Patterns = data.Patterns,
                IsReadOnly = data.IsReadOnly,
                Value = null,
                BoundingRectangle = data.BoundingRectangle,
                NativeWindowHandle = data.NativeWindowHandle,
                Children = children,
            };
    }
}
