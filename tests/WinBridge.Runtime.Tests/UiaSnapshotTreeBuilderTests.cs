using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class UiaSnapshotTreeBuilderTests
{
    [Fact]
    public void BuildCreatesExpectedSnapshotTree()
    {
        FakeNode grandchild = new(
            new UiaSnapshotNodeData(
                RuntimeId: null,
                Name: "Save",
                AutomationId: "SaveButton",
                ClassName: "Button",
                FrameworkId: "Win32",
                ControlType: "button",
                ControlTypeId: 50000,
                LocalizedControlType: "кнопка",
                IsControlElement: true,
                IsContentElement: true,
                IsEnabled: true,
                IsOffscreen: false,
                HasKeyboardFocus: false,
                IsPassword: false,
                IsReadOnly: null,
                Patterns: ["invoke"],
                BoundingRectangle: new Bounds(20, 20, 40, 40),
                NativeWindowHandle: 42));
        FakeNode child = new(
            new UiaSnapshotNodeData(
                RuntimeId: [3, 4],
                Name: "Toolbar",
                AutomationId: "MainToolbar",
                ClassName: "ToolbarWindow32",
                FrameworkId: "Win32",
                ControlType: "tool_bar",
                ControlTypeId: 50021,
                LocalizedControlType: "панель инструментов",
                IsControlElement: true,
                IsContentElement: true,
                IsEnabled: true,
                IsOffscreen: false,
                HasKeyboardFocus: false,
                IsPassword: false,
                IsReadOnly: null,
                Patterns: ["expand_collapse", "invoke"],
                BoundingRectangle: new Bounds(10, 10, 110, 40),
                NativeWindowHandle: 41),
            [grandchild]);
        FakeNode root = new(
            new UiaSnapshotNodeData(
                RuntimeId: [1, 2],
                Name: "Calculator",
                AutomationId: "Root",
                ClassName: "ApplicationFrameWindow",
                FrameworkId: "Win32",
                ControlType: "window",
                ControlTypeId: 50032,
                LocalizedControlType: "окно",
                IsControlElement: true,
                IsContentElement: true,
                IsEnabled: true,
                IsOffscreen: false,
                HasKeyboardFocus: true,
                IsPassword: false,
                IsReadOnly: null,
                Patterns: ["window"],
                BoundingRectangle: new Bounds(0, 0, 400, 300),
                NativeWindowHandle: 40),
            [child]);

        UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult result = UiaSnapshotTreeBuilder.Build(
            root,
            maxDepth: 3,
            maxNodes: 10,
            CancellationToken.None);

        Assert.Equal(3, result.NodeCount);
        Assert.Equal(2, result.RealizedDepth);
        Assert.False(result.Truncated);
        Assert.False(result.NodeBudgetBoundaryReached);
        Assert.Equal("rid:1.2", result.Root.ElementId);
        Assert.Null(result.Root.ParentElementId);
        Assert.Equal("window", result.Root.ControlType);
        Assert.Equal(50032, result.Root.ControlTypeId);
        Assert.Equal("окно", result.Root.LocalizedControlType);
        Assert.True(result.Root.HasKeyboardFocus);
        Assert.Null(result.Root.Value);
        Assert.Single(result.Root.Children);

        UiaElementSnapshot childSnapshot = result.Root.Children[0];
        Assert.Equal("rid:3.4", childSnapshot.ElementId);
        Assert.Equal("rid:1.2", childSnapshot.ParentElementId);
        Assert.Equal(1, childSnapshot.Depth);
        Assert.Equal(0, childSnapshot.Ordinal);
        Assert.Equal(["expand_collapse", "invoke"], childSnapshot.Patterns);
        Assert.Single(childSnapshot.Children);

        UiaElementSnapshot grandchildSnapshot = childSnapshot.Children[0];
        Assert.Equal("path:0/0/0", grandchildSnapshot.ElementId);
        Assert.Equal("rid:3.4", grandchildSnapshot.ParentElementId);
        Assert.Equal(2, grandchildSnapshot.Depth);
        Assert.Equal(0, grandchildSnapshot.Ordinal);
        Assert.Equal("button", grandchildSnapshot.ControlType);
        Assert.Equal(["invoke"], grandchildSnapshot.Patterns);
    }

    [Fact]
    public void BuildReturnsOnlyRootWhenDepthIsZeroAndMarksTruncation()
    {
        FakeNode root = new(CreateNodeData("window", runtimeId: [1]), [new FakeNode(CreateNodeData("button", runtimeId: [2]))]);

        UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult result = UiaSnapshotTreeBuilder.Build(
            root,
            maxDepth: 0,
            maxNodes: 10,
            CancellationToken.None);

        Assert.Equal(1, result.NodeCount);
        Assert.Equal(0, result.RealizedDepth);
        Assert.False(result.Truncated);
        Assert.True(result.DepthBoundaryReached);
        Assert.False(result.NodeBudgetBoundaryReached);
        Assert.Empty(result.Root.Children);
    }

    [Fact]
    public void BuildHonorsMaxNodesAndMarksTruncation()
    {
        FakeNode root = new(
            CreateNodeData("window", runtimeId: [1]),
            [
                new FakeNode(CreateNodeData("button", runtimeId: [2])),
                new FakeNode(CreateNodeData("button", runtimeId: [3])),
            ]);

        UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult result = UiaSnapshotTreeBuilder.Build(
            root,
            maxDepth: 3,
            maxNodes: 2,
            CancellationToken.None);

        Assert.Equal(2, result.NodeCount);
        Assert.Equal(1, result.RealizedDepth);
        Assert.True(result.Truncated);
        Assert.False(result.DepthBoundaryReached);
        Assert.True(result.NodeBudgetBoundaryReached);
        Assert.Single(result.Root.Children);
        Assert.Equal("rid:2", result.Root.Children[0].ElementId);
    }

    [Fact]
    public void BuildDoesNotMarkTruncationWhenBudgetMatchesTreeExactly()
    {
        FakeNode root = new(
            CreateNodeData("window", runtimeId: [1]),
            [new FakeNode(CreateNodeData("button", runtimeId: [2]))]);

        UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult result = UiaSnapshotTreeBuilder.Build(
            root,
            maxDepth: 3,
            maxNodes: 2,
            CancellationToken.None);

        Assert.Equal(2, result.NodeCount);
        Assert.False(result.Truncated);
        Assert.False(result.DepthBoundaryReached);
        Assert.True(result.NodeBudgetBoundaryReached);
    }

    [Fact]
    public void BuildDoesNotProbeChildrenWhenDepthBoundaryIsReached()
    {
        BoundaryGuardNode root = new(CreateNodeData("window", runtimeId: [1]));

        UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult result = UiaSnapshotTreeBuilder.Build(
            root,
            maxDepth: 0,
            maxNodes: 10,
            CancellationToken.None);

        Assert.Equal(1, result.NodeCount);
        Assert.False(result.Truncated);
        Assert.True(result.DepthBoundaryReached);
        Assert.False(result.NodeBudgetBoundaryReached);
    }

    [Fact]
    public void BuildDoesNotProbeChildrenWhenNodeBudgetIsExhausted()
    {
        BoundaryGuardNode root = new(CreateNodeData("window", runtimeId: [1]));

        UiaSnapshotTreeBuilder.UiaSnapshotTreeBuildResult result = UiaSnapshotTreeBuilder.Build(
            root,
            maxDepth: 3,
            maxNodes: 1,
            CancellationToken.None);

        Assert.Equal(1, result.NodeCount);
        Assert.False(result.Truncated);
        Assert.False(result.DepthBoundaryReached);
        Assert.True(result.NodeBudgetBoundaryReached);
    }

    private static UiaSnapshotNodeData CreateNodeData(string controlType, int[]? runtimeId) =>
        new(
            RuntimeId: runtimeId,
            Name: controlType,
            AutomationId: controlType,
            ClassName: controlType,
            FrameworkId: "Win32",
            ControlType: controlType,
            ControlTypeId: 50000,
            LocalizedControlType: controlType,
            IsControlElement: true,
            IsContentElement: true,
            IsEnabled: true,
            IsOffscreen: false,
            HasKeyboardFocus: false,
            IsPassword: false,
            IsReadOnly: null,
            Patterns: [],
            BoundingRectangle: new Bounds(0, 0, 10, 10),
            NativeWindowHandle: null);

    private sealed class FakeNode(UiaSnapshotNodeData data, IReadOnlyList<FakeNode>? children = null) : IUiaSnapshotNode
    {
        private readonly IReadOnlyList<FakeNode> _children = children ?? [];
        private int _siblingIndex = -1;
        private IReadOnlyList<FakeNode>? _siblings;

        public UiaSnapshotNodeData GetData() => data;

        public IUiaSnapshotNode? GetFirstChild()
        {
            if (_children.Count == 0)
            {
                return null;
            }

            for (int index = 0; index < _children.Count; index++)
            {
                _children[index]._siblings = _children;
                _children[index]._siblingIndex = index;
            }

            return _children[0];
        }

        public IUiaSnapshotNode? GetNextSibling()
        {
            if (_siblings is null)
            {
                return null;
            }

            int nextIndex = _siblingIndex + 1;
            return nextIndex < _siblings.Count ? _siblings[nextIndex] : null;
        }
    }

    private sealed class BoundaryGuardNode(UiaSnapshotNodeData data) : IUiaSnapshotNode
    {
        public UiaSnapshotNodeData GetData() => data;

        public IUiaSnapshotNode? GetFirstChild() =>
            throw new Xunit.Sdk.XunitException("Tree builder не должен зондировать child nodes после достижения maxDepth.");

        public IUiaSnapshotNode? GetNextSibling() => null;
    }
}
