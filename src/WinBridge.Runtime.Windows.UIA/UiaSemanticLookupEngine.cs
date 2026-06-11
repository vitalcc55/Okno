// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal interface IUiaSemanticLookupNode
{
    UiaSnapshotNodeData GetData();

    IUiaSemanticLookupNode? GetFirstChild();

    IUiaSemanticLookupNode? GetNextSibling();
}

internal sealed class UiaSemanticLookupEngine(TimeProvider timeProvider)
{
    public UiaSemanticLookupResult Search(
        IUiaSemanticLookupNode root,
        ObservedWindowDescriptor observedWindow,
        UiaSemanticLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(observedWindow);
        ArgumentNullException.ThrowIfNull(request);

        if (!UiaSemanticLookupRequestValidator.TryValidate(request, out string? validationReason))
        {
            return Failed(
                UiaSemanticLookupFailureKindValues.InvalidRequest,
                validationReason!,
                request);
        }

        WaitElementSelector selector = request.Selector!;
        long startTimestamp = timeProvider.GetTimestamp();
        SearchContext context = new(observedWindow, request, selector, startTimestamp, cancellationToken);

        try
        {
            UiaSemanticLookupResult? terminal = SearchNode(
                root,
                parentElementId: null,
                depth: 0,
                ordinal: 0,
                rawPath: "0",
                context);
            if (terminal is not null)
            {
                return terminal;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return new UiaSemanticLookupResult(
                Status: UiaSemanticLookupStatusValues.Failed,
                Window: observedWindow,
                VisitedNodeCount: context.VisitedNodeCount,
                MatchCount: context.MatchCount,
                MatchCardinality: ElementSelectorPolicy.ClassifyMatchCount(context.MatchCount),
                MaxDepth: request.MaxDepth,
                MaxNodes: request.MaxNodes,
                FailureKind: UiaSemanticLookupFailureKindValues.ProviderFailure,
                Reason: "UI Automation не смогла выполнить bounded semantic lookup traversal.");
        }

        return context.UniqueMatch is null
            ? Terminal(
                UiaSemanticLookupStatusValues.ZeroMatches,
                observedWindow,
                request,
                context.VisitedNodeCount,
                context.MatchCount,
                reason: "Semantic lookup не нашёл UIA target для selector.")
            : new UiaSemanticLookupResult(
                Status: UiaSemanticLookupStatusValues.UniqueMatch,
                Window: observedWindow,
                Element: context.UniqueMatch,
                VisitedNodeCount: context.VisitedNodeCount,
                MatchCount: context.MatchCount,
                MatchCardinality: ElementSelectorPolicy.ClassifyMatchCount(context.MatchCount),
                MaxDepth: request.MaxDepth,
                MaxNodes: request.MaxNodes);
    }

    private UiaSemanticLookupResult? SearchNode(
        IUiaSemanticLookupNode node,
        string? parentElementId,
        int depth,
        int ordinal,
        string rawPath,
        SearchContext context)
    {
        if (TryCreateTraversalBoundaryResult(context, out UiaSemanticLookupResult? terminal))
        {
            return terminal;
        }

        context.VisitedNodeCount++;
        UiaSnapshotNodeData data;
        try
        {
            data = node.GetData();
        }
        catch (Exception exception) when (IsProviderFailure(exception) && depth > 0)
        {
            return null;
        }

        string elementId = CreateElementId(data.RuntimeId, rawPath);

        if (ElementSelectorPolicy.Matches(context.Selector, data.Name, data.AutomationId, data.ControlType))
        {
            context.MatchCount++;
            if (ElementSelectorPolicy.IsAmbiguous(context.MatchCount))
            {
                return Terminal(
                    UiaSemanticLookupStatusValues.AmbiguousMatches,
                    context.ObservedWindow,
                    context.Request,
                    context.VisitedNodeCount,
                    context.MatchCount,
                    reason: "Semantic lookup нашёл больше одного UIA target для selector.");
            }

            context.UniqueMatch = CreateLeafSnapshot(
                data,
                elementId,
                parentElementId,
                depth,
                ordinal);
        }

        if (TryCreateTraversalBoundaryResult(context, out terminal))
        {
            return terminal;
        }

        IUiaSemanticLookupNode? child;
        try
        {
            child = node.GetFirstChild();
        }
        catch (Exception exception) when (IsProviderFailure(exception) && depth > 0)
        {
            return null;
        }

        if (child is null)
        {
            return null;
        }

        if (depth >= context.Request.MaxDepth)
        {
            return Terminal(
                UiaSemanticLookupStatusValues.BudgetExceeded,
                context.ObservedWindow,
                context.Request,
                context.VisitedNodeCount,
                context.MatchCount,
                depthBudgetExceeded: true,
                reason: "Semantic lookup достиг maxDepth boundary до доказательства отсутствия или уникальности target.");
        }

        int childOrdinal = 0;
        while (child is not null)
        {
            string childPath = rawPath + "/" + childOrdinal.ToString(CultureInfo.InvariantCulture);
            terminal = SearchNode(child, elementId, depth + 1, childOrdinal, childPath, context);
            if (terminal is not null)
            {
                return terminal;
            }

            if (TryCreateTraversalBoundaryResult(context, out terminal))
            {
                return terminal;
            }

            try
            {
                child = child.GetNextSibling();
            }
            catch (Exception exception) when (IsProviderFailure(exception) && depth > 0)
            {
                return null;
            }

            childOrdinal++;
        }

        return null;
    }

    private bool TryCreateTraversalBoundaryResult(SearchContext context, out UiaSemanticLookupResult? result)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (IsTimedOut(context.StartTimestamp, context.Request.TimeoutMs))
        {
            result = Terminal(
                UiaSemanticLookupStatusValues.Timeout,
                context.ObservedWindow,
                context.Request,
                context.VisitedNodeCount,
                context.MatchCount,
                reason: "Semantic lookup превысил timeout budget.");
            return true;
        }

        if (context.VisitedNodeCount >= context.Request.MaxNodes)
        {
            result = Terminal(
                UiaSemanticLookupStatusValues.BudgetExceeded,
                context.ObservedWindow,
                context.Request,
                context.VisitedNodeCount,
                context.MatchCount,
                nodeBudgetExceeded: true,
                reason: "Semantic lookup достиг maxNodes budget до доказательства уникальности target.");
            return true;
        }

        result = null;
        return false;
    }

    private bool IsTimedOut(long startTimestamp, int timeoutMs) =>
        timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds >= timeoutMs;

    private static bool IsProviderFailure(Exception exception) =>
        exception is ElementNotAvailableException
        || exception is InvalidOperationException
        || exception is COMException;

    private static UiaSemanticLookupResult Failed(
        string failureKind,
        string reason,
        UiaSemanticLookupRequest request,
        ObservedWindowDescriptor? observedWindow = null) =>
        new(
            Status: UiaSemanticLookupStatusValues.Failed,
            Window: observedWindow,
            MaxDepth: request.MaxDepth,
            MaxNodes: request.MaxNodes,
            FailureKind: failureKind,
            Reason: reason);

    private static UiaSemanticLookupResult Terminal(
        string status,
        ObservedWindowDescriptor observedWindow,
        UiaSemanticLookupRequest request,
        int visitedNodeCount,
        int matchCount,
        bool nodeBudgetExceeded = false,
        bool depthBudgetExceeded = false,
        string? reason = null) =>
        new(
            Status: status,
            Window: observedWindow,
            VisitedNodeCount: visitedNodeCount,
            MatchCount: matchCount,
            MatchCardinality: ElementSelectorPolicy.ClassifyMatchCount(matchCount),
            NodeBudgetExceeded: nodeBudgetExceeded,
            DepthBudgetExceeded: depthBudgetExceeded,
            MaxDepth: request.MaxDepth,
            MaxNodes: request.MaxNodes,
            Reason: reason);

    private static string CreateElementId(int[]? runtimeId, string rawPath) =>
        runtimeId is { Length: > 0 }
            ? "rid:" + string.Join(".", runtimeId) + ";raw:" + rawPath
            : "raw:" + rawPath;

    private static UiaElementSnapshot CreateLeafSnapshot(
        UiaSnapshotNodeData data,
        string elementId,
        string? parentElementId,
        int depth,
        int ordinal) =>
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
            Value = null,
            BoundingRectangle = data.BoundingRectangle,
            NativeWindowHandle = data.NativeWindowHandle,
            Children = [],
        };

    private sealed class SearchContext(
        ObservedWindowDescriptor observedWindow,
        UiaSemanticLookupRequest request,
        WaitElementSelector selector,
        long startTimestamp,
        CancellationToken cancellationToken)
    {
        public ObservedWindowDescriptor ObservedWindow { get; } = observedWindow;

        public UiaSemanticLookupRequest Request { get; } = request;

        public WaitElementSelector Selector { get; } = selector;

        public long StartTimestamp { get; } = startTimestamp;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public int VisitedNodeCount { get; set; }

        public int MatchCount { get; set; }

        public UiaElementSnapshot? UniqueMatch { get; set; }
    }
}
