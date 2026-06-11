// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class UiaSemanticLookupEngineTests
{
    [Fact]
    public void SearchFindsUniqueDeepRawDescendant()
    {
        FakeSemanticLookupNode target = Node("target", automationId: "DeepTarget", controlType: "edit", runtimeId: [4, 2]);
        FakeSemanticLookupNode root = Node(
            "root",
            controlType: "window",
            children:
            [
                Node("container", children: [Node("inner", children: [target])]),
            ]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "DeepTarget", ControlType: "edit"),
                MaxDepth = 4,
                MaxNodes = 16,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.UniqueMatch, result.Status);
        Assert.Equal(ElementSelectorMatchCardinalityValues.Unique, result.MatchCardinality);
        Assert.Equal("DeepTarget", result.Element?.AutomationId);
        Assert.Equal("edit", result.Element?.ControlType);
        Assert.Equal(4, result.VisitedNodeCount);
        Assert.False(result.NodeBudgetExceeded);
        Assert.False(result.DepthBudgetExceeded);
        Assert.StartsWith("rid:4.2;raw:", result.Element!.ElementId, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchReturnsZeroMatchesWhenTraversalCompletes()
    {
        FakeSemanticLookupNode root = Node("root", children: [Node("button", automationId: "Run")]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Missing"),
                MaxDepth = 3,
                MaxNodes = 8,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.ZeroMatches, result.Status);
        Assert.Equal(ElementSelectorMatchCardinalityValues.None, result.MatchCardinality);
        Assert.Null(result.Element);
        Assert.Equal(2, result.VisitedNodeCount);
    }

    [Fact]
    public void SearchFailsClosedWhenSelectorIsAmbiguous()
    {
        FakeSemanticLookupNode root = Node(
            "root",
            children:
            [
                Node("first", automationId: "Duplicate", controlType: "button"),
                Node("second", automationId: "Duplicate", controlType: "button"),
            ]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Duplicate", ControlType: "button"),
                MaxDepth = 2,
                MaxNodes = 8,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.AmbiguousMatches, result.Status);
        Assert.Equal(ElementSelectorMatchCardinalityValues.Ambiguous, result.MatchCardinality);
        Assert.Null(result.Element);
        Assert.Equal(2, result.MatchCount);
    }

    [Fact]
    public void SearchAbortsWhenNodeBudgetCannotProveUniqueness()
    {
        FakeSemanticLookupNode root = Node(
            "root",
            children:
            [
                Node("first", automationId: "Target"),
                Node("unvisited"),
            ]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Target"),
                MaxDepth = 2,
                MaxNodes = 2,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.BudgetExceeded, result.Status);
        Assert.True(result.NodeBudgetExceeded);
        Assert.Equal(2, result.VisitedNodeCount);
    }

    [Fact]
    public void SearchDoesNotEnumerateChildrenAfterNodeBudgetIsExhausted()
    {
        ThrowingChildrenSemanticLookupNode root = new(CreateNodeData("root"));

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Missing"),
                MaxDepth = 2,
                MaxNodes = 1,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.BudgetExceeded, result.Status);
        Assert.True(result.NodeBudgetExceeded);
        Assert.Equal(1, result.VisitedNodeCount);
    }

    [Fact]
    public void SearchAbortsWhenDepthBudgetCannotProveAbsence()
    {
        FakeSemanticLookupNode root = Node(
            "root",
            children:
            [
                Node("container", children: [Node("hidden", automationId: "Target")]),
            ]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Target"),
                MaxDepth = 1,
                MaxNodes = 8,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.BudgetExceeded, result.Status);
        Assert.True(result.DepthBudgetExceeded);
        Assert.Equal(2, result.VisitedNodeCount);
    }

    [Fact]
    public void SearchRejectsSelectorWithoutCriteria()
    {
        UiaSemanticLookupResult result = Search(
            Node("root"),
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(),
            });

        Assert.Equal(UiaSemanticLookupStatusValues.Failed, result.Status);
        Assert.Equal(UiaSemanticLookupFailureKindValues.InvalidRequest, result.FailureKind);
        Assert.Equal(0, result.VisitedNodeCount);
    }

    [Fact]
    public void SearchReturnsTimeoutWhenTraversalExceedsTimeBudget()
    {
        FakeSemanticLookupNode root = Node("root", children: [Node("child")]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Missing"),
                TimeoutMs = 1,
            },
            new AdvancingTimeProvider());

        Assert.Equal(UiaSemanticLookupStatusValues.Timeout, result.Status);
        Assert.Equal(0, result.VisitedNodeCount);
    }

    [Fact]
    public void SearchHonorsCancellationBeforeTraversal()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Search(
            Node("root"),
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Target"),
            },
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public void SearchReturnsProviderFailureWhenTraversalNodeThrows()
    {
        UiaSemanticLookupResult result = Search(
            new ThrowingSemanticLookupNode(),
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "Target"),
            });

        Assert.Equal(UiaSemanticLookupStatusValues.Failed, result.Status);
        Assert.Equal(UiaSemanticLookupFailureKindValues.ProviderFailure, result.FailureKind);
        Assert.DoesNotContain("secret provider failure", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchSkipsNonRootProviderFailureAndContinuesSiblingTraversal()
    {
        FakeSemanticLookupNode target = Node("target", automationId: "DeepTarget", controlType: "button");
        FakeSemanticLookupNode root = Node(
            "root",
            controlType: "window",
            children:
            [
                new ThrowingSemanticLookupNode(),
                target,
            ]);

        UiaSemanticLookupResult result = Search(
            root,
            new UiaSemanticLookupRequest
            {
                Selector = new WaitElementSelector(AutomationId: "DeepTarget", ControlType: "button"),
                MaxDepth = 2,
                MaxNodes = 8,
            });

        Assert.Equal(UiaSemanticLookupStatusValues.UniqueMatch, result.Status);
        Assert.Equal("DeepTarget", result.Element?.AutomationId);
        Assert.Equal(3, result.VisitedNodeCount);
    }

    private static UiaSemanticLookupResult Search(
        FakeSemanticLookupNode root,
        UiaSemanticLookupRequest request,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default) =>
        Search((IUiaSemanticLookupNode)root, request, timeProvider, cancellationToken);

    private static UiaSemanticLookupResult Search(
        IUiaSemanticLookupNode root,
        UiaSemanticLookupRequest request,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default) =>
        new UiaSemanticLookupEngine(timeProvider ?? TimeProvider.System).Search(
            root,
            CreateObservedWindow(),
            request,
            cancellationToken);

    private static FakeSemanticLookupNode Node(
        string name,
        string? automationId = null,
        string controlType = "pane",
        int[]? runtimeId = null,
        IUiaSemanticLookupNode[]? children = null)
    {
        LinkSiblings(children ?? []);
        return new(
            CreateNodeData(name, automationId, controlType, runtimeId),
            children ?? []);
    }

    private static void LinkSiblings(IUiaSemanticLookupNode[] children)
    {
        for (int index = 0; index < children.Length; index++)
        {
            IUiaSemanticLookupNode? nextSibling = index + 1 < children.Length
                ? children[index + 1]
                : null;
            if (children[index] is LinkedTestSemanticLookupNode linked)
            {
                linked.NextSibling = nextSibling;
            }
        }
    }

    private static UiaSnapshotNodeData CreateNodeData(
        string name,
        string? automationId = null,
        string controlType = "pane",
        int[]? runtimeId = null) =>
        new(
            RuntimeId: runtimeId,
            Name: name,
            AutomationId: automationId,
            ClassName: null,
            FrameworkId: null,
            ControlType: controlType,
            ControlTypeId: 0,
            LocalizedControlType: null,
            IsControlElement: true,
            IsContentElement: true,
            IsEnabled: true,
            IsOffscreen: false,
            HasKeyboardFocus: false,
            IsPassword: false,
            IsReadOnly: null,
            Patterns: [],
            BoundingRectangle: null,
            NativeWindowHandle: null);

    private static ObservedWindowDescriptor CreateObservedWindow() =>
        new(101, Title: "Lookup test window");

    private abstract class LinkedTestSemanticLookupNode : IUiaSemanticLookupNode
    {
        public IUiaSemanticLookupNode? NextSibling { get; set; }

        public abstract UiaSnapshotNodeData GetData();

        public abstract IUiaSemanticLookupNode? GetFirstChild();

        public IUiaSemanticLookupNode? GetNextSibling() => NextSibling;
    }

    private sealed class FakeSemanticLookupNode(
        UiaSnapshotNodeData data,
        IReadOnlyList<IUiaSemanticLookupNode> children) : LinkedTestSemanticLookupNode
    {
        public override UiaSnapshotNodeData GetData() => data;

        public override IUiaSemanticLookupNode? GetFirstChild() =>
            children.Count == 0 ? null : children[0];
    }

    private sealed class ThrowingSemanticLookupNode : LinkedTestSemanticLookupNode
    {
        public override UiaSnapshotNodeData GetData() =>
            throw new InvalidOperationException("secret provider failure");

        public override IUiaSemanticLookupNode? GetFirstChild() => null;
    }

    private sealed class ThrowingChildrenSemanticLookupNode(UiaSnapshotNodeData data) : LinkedTestSemanticLookupNode
    {
        public override UiaSnapshotNodeData GetData() => data;

        public override IUiaSemanticLookupNode? GetFirstChild() =>
            throw new InvalidOperationException("semantic lookup enumerated children after node budget exhaustion");
    }

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            _timestamp += TimeSpan.FromMilliseconds(2).Ticks;
            return _timestamp;
        }
    }
}
