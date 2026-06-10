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

    IReadOnlyList<IUiaSemanticLookupNode> GetChildren();
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
        Stack<TraversalNode> pending = [];
        pending.Push(new(root, ParentElementId: null, Depth: 0, Ordinal: 0, RawPath: "0"));
        int visitedNodeCount = 0;
        int matchCount = 0;
        UiaElementSnapshot? uniqueMatch = null;

        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsTimedOut(startTimestamp, request.TimeoutMs))
                {
                    return Terminal(
                        UiaSemanticLookupStatusValues.Timeout,
                        observedWindow,
                        request,
                        visitedNodeCount,
                        matchCount,
                        reason: "Semantic lookup превысил timeout budget.");
                }

                if (visitedNodeCount >= request.MaxNodes)
                {
                    return Terminal(
                        UiaSemanticLookupStatusValues.BudgetExceeded,
                        observedWindow,
                        request,
                        visitedNodeCount,
                        matchCount,
                        nodeBudgetExceeded: true,
                        reason: "Semantic lookup достиг maxNodes budget до доказательства уникальности target.");
                }

                TraversalNode current = pending.Pop();
                visitedNodeCount++;
                UiaSnapshotNodeData data = current.Node.GetData();
                string elementId = CreateElementId(data.RuntimeId, current.RawPath);

                if (ElementSelectorPolicy.Matches(selector, data.Name, data.AutomationId, data.ControlType))
                {
                    matchCount++;
                    if (ElementSelectorPolicy.IsAmbiguous(matchCount))
                    {
                        return Terminal(
                            UiaSemanticLookupStatusValues.AmbiguousMatches,
                            observedWindow,
                            request,
                            visitedNodeCount,
                            matchCount,
                            reason: "Semantic lookup нашёл больше одного UIA target для selector.");
                    }

                    uniqueMatch = CreateLeafSnapshot(
                        data,
                        elementId,
                        current.ParentElementId,
                        current.Depth,
                        current.Ordinal);
                }

                IReadOnlyList<IUiaSemanticLookupNode> children = current.Node.GetChildren();
                if (children.Count == 0)
                {
                    continue;
                }

                if (current.Depth >= request.MaxDepth)
                {
                    return Terminal(
                        UiaSemanticLookupStatusValues.BudgetExceeded,
                        observedWindow,
                        request,
                        visitedNodeCount,
                        matchCount,
                        depthBudgetExceeded: true,
                        reason: "Semantic lookup достиг maxDepth boundary до доказательства отсутствия или уникальности target.");
                }

                for (int index = children.Count - 1; index >= 0; index--)
                {
                    string childPath = current.RawPath + "/" + index.ToString(CultureInfo.InvariantCulture);
                    string childParentId = elementId;
                    pending.Push(new(children[index], childParentId, current.Depth + 1, index, childPath));
                }
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
                VisitedNodeCount: visitedNodeCount,
                MatchCount: matchCount,
                MatchCardinality: ElementSelectorPolicy.ClassifyMatchCount(matchCount),
                MaxDepth: request.MaxDepth,
                MaxNodes: request.MaxNodes,
                FailureKind: UiaSemanticLookupFailureKindValues.ProviderFailure,
                Reason: "UI Automation не смогла выполнить bounded semantic lookup traversal.");
        }

        return uniqueMatch is null
            ? Terminal(
                UiaSemanticLookupStatusValues.ZeroMatches,
                observedWindow,
                request,
                visitedNodeCount,
                matchCount,
                reason: "Semantic lookup не нашёл UIA target для selector.")
            : new UiaSemanticLookupResult(
                Status: UiaSemanticLookupStatusValues.UniqueMatch,
                Window: observedWindow,
                Element: uniqueMatch,
                VisitedNodeCount: visitedNodeCount,
                MatchCount: matchCount,
                MatchCardinality: ElementSelectorPolicy.ClassifyMatchCount(matchCount),
                MaxDepth: request.MaxDepth,
                MaxNodes: request.MaxNodes);
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

    private sealed record TraversalNode(
        IUiaSemanticLookupNode Node,
        string? ParentElementId,
        int Depth,
        int Ordinal,
        string RawPath);
}
