using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class Win32UiAutomationWaitProbe : IUiAutomationWaitProbe
{
    private static readonly int ElementNotAvailableHResult = new ElementNotAvailableException().HResult;

    public async Task<UiAutomationWaitProbeExecutionResult> ProbeAsync(
        WindowDescriptor targetWindow,
        WaitRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        new(
            await UiAutomationMtaRunner.RunAsync(
                cancellationTokenInner => ProbeCore(targetWindow, request, cancellationTokenInner),
                cancellationToken).ConfigureAwait(false),
            DateTimeOffset.UtcNow,
            TimedOut: false,
            DiagnosticArtifactPath: null);

    private static UiAutomationWaitProbeResult ProbeCore(
        WindowDescriptor targetWindow,
        WaitRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Selector is null)
        {
            return Failed("UI Automation wait probe требует selector.");
        }

        CacheRequest cacheRequest = AutomationSnapshotNode.CreateControlViewCacheRequest();
        AutomationElement root;
        try
        {
            using (cacheRequest.Activate())
            {
                root = AutomationElement.FromHandle(new IntPtr(targetWindow.Hwnd));
            }
        }
        catch (ElementNotAvailableException)
        {
            return Failed("UI Automation не смогла получить root element для выбранного hwnd.");
        }
        catch (ArgumentException)
        {
            return Failed("UI Automation не смогла получить root element для выбранного hwnd.");
        }
        catch (COMException)
        {
            return Failed("UI Automation не смогла получить root element для выбранного hwnd.");
        }

        try
        {
            AutomationSnapshotNode rootNode = new(root, cacheRequest);
            ObservedWindowDescriptor observedWindow = ObservedWindowBuilder.Create(targetWindow, root, rootNode.GetData());

            if (string.Equals(request.Condition, WaitConditionValues.FocusIs, StringComparison.Ordinal))
            {
                return ProbeFocusedElement(rootNode, cacheRequest, observedWindow, request.Selector!, cancellationToken);
            }

            UiAutomationWaitMatchAccumulator matches = new(request.Condition, request.ExpectedText);

            Stack<TraversalNode> pending = new();
            pending.Push(new(rootNode, null, 0, 0, "0"));

            while (pending.Count > 0 && matches.ShouldContinueTraversal)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TraversalNode current = pending.Pop();
                UiaSnapshotNodeData data = current.Node.GetData();
                string elementId = UiaElementIdBuilder.Create(data.RuntimeId, current.Path);

                if (SelectorMatches(data, request.Selector))
                {
                    UiaElementSnapshot snapshot = CreateLeafSnapshot(data, elementId, current.ParentElementId, current.Depth, current.Ordinal);
                    matches.AddSelectorHit(
                        snapshot,
                        TryGetValueText(current.Node.Element),
                        TryGetTextPatternText(current.Node.Element));
                }

                List<TraversalNode> children = [];
                if (current.Node.GetFirstChild() is AutomationSnapshotNode child)
                {
                    int childOrdinal = 0;
                    for (AutomationSnapshotNode? childNode = child;
                        childNode is not null;
                        childNode = childNode.GetNextSibling() as AutomationSnapshotNode)
                    {
                        string childPath = current.Path + "/" + childOrdinal.ToString(CultureInfo.InvariantCulture);
                        children.Add(new(childNode, elementId, current.Depth + 1, childOrdinal, childPath));
                        childOrdinal++;
                    }
                }

                for (int index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }

            return matches.Build(observedWindow);
        }
        catch (InvalidOperationException)
        {
            return Failed("UI Automation не смогла материализовать wait probe для выбранного hwnd.");
        }
        catch (ElementNotAvailableException)
        {
            return Failed("UI Automation не смогла материализовать wait probe для выбранного hwnd.");
        }
        catch (COMException)
        {
            return Failed("UI Automation не смогла материализовать wait probe для выбранного hwnd.");
        }
    }

    private static bool SelectorMatches(UiaSnapshotNodeData data, WaitElementSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Name)
            && !string.Equals(data.Name, selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.AutomationId)
            && !string.Equals(data.AutomationId, selector.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ControlType)
            && !string.Equals(data.ControlType, selector.ControlType, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

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

    private static UiAutomationWaitProbeResult ProbeFocusedElement(
        AutomationSnapshotNode rootNode,
        CacheRequest cacheRequest,
        ObservedWindowDescriptor observedWindow,
        WaitElementSelector selector,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FocusProbeAttemptResult probeAttempt = TryProbeFocusedElement(rootNode, cacheRequest, selector);
            if (probeAttempt.Outcome == FocusProbeOutcome.TransientUnavailable && attempt < 2)
            {
                if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(15)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                continue;
            }

            if (probeAttempt.Outcome == FocusProbeOutcome.Failed)
            {
                return new UiAutomationWaitProbeResult
                {
                    Window = observedWindow,
                    Matches = [],
                    Reason = probeAttempt.FailureReason,
                    FailureStage = "focus_probe",
                };
            }

            return new UiAutomationWaitProbeResult
            {
                Window = observedWindow,
                Matches = probeAttempt.Match is null ? [] : [probeAttempt.Match],
            };
        }

        return new UiAutomationWaitProbeResult
        {
            Window = observedWindow,
            Matches = [],
        };
    }

    private static FocusProbeAttemptResult TryProbeFocusedElement(
        AutomationSnapshotNode rootNode,
        CacheRequest cacheRequest,
        WaitElementSelector selector)
    {
        try
        {
            AutomationElement? focusedElement;
            using (cacheRequest.Activate())
            {
                focusedElement = AutomationElement.FocusedElement;
            }

            if (focusedElement is null)
            {
                return new(FocusProbeOutcome.NotMatched, null);
            }

            FocusedElementPathResolution? pathResolution = TryResolveFocusedElementPath(rootNode, cacheRequest, focusedElement);
            if (pathResolution is null)
            {
                return new(FocusProbeOutcome.NotMatched, null);
            }

            AutomationSnapshotNode focusedNode = new(focusedElement, cacheRequest);
            UiaSnapshotNodeData focusedData = focusedNode.GetData();
            if (!SelectorMatches(focusedData, selector))
            {
                return new(FocusProbeOutcome.NotMatched, null);
            }

            UiaSnapshotNodeData? parentData = pathResolution.ImmediateParentElement is null
                ? null
                : new AutomationSnapshotNode(pathResolution.ImmediateParentElement, cacheRequest).GetData();
            string elementId = CreateFocusedElementId(focusedData.RuntimeId, pathResolution.Path);
            string? parentElementId = parentData is null || string.Equals(pathResolution.Path, "0", StringComparison.Ordinal)
                ? null
                : CreateFocusedElementId(parentData.RuntimeId, GetParentPath(pathResolution.Path));

            return new(
                FocusProbeOutcome.Matched,
                CreateLeafSnapshot(
                    focusedData,
                    elementId,
                    parentElementId,
                    GetDepth(pathResolution.Path),
                    GetOrdinal(pathResolution.Path)));
        }
        catch (Exception exception) when (IsTransientFocusedElementError(exception))
        {
            return new(FocusProbeOutcome.TransientUnavailable, null);
        }
        catch (InvalidOperationException)
        {
            return new(FocusProbeOutcome.Failed, null, "UI Automation не смогла материализовать focused element для выбранного hwnd.");
        }
        catch (COMException)
        {
            return new(FocusProbeOutcome.Failed, null, "UI Automation не смогла материализовать focused element для выбранного hwnd.");
        }
    }

    internal static bool IsTransientFocusedElementError(Exception exception) =>
        exception is ElementNotAvailableException
        || exception is COMException comException && comException.HResult == ElementNotAvailableHResult;

    private static FocusedElementPathResolution? TryResolveFocusedElementPath(
        AutomationSnapshotNode rootNode,
        CacheRequest cacheRequest,
        AutomationElement focusedElement)
    {
        if (ElementsEqual(rootNode.Element, focusedElement, cacheRequest))
        {
            return new FocusedElementPathResolution("0", null);
        }

        List<int> ordinals = [];
        AutomationElement current = focusedElement;
        AutomationElement? immediateParent = null;

        while (true)
        {
            AutomationElement? parent = TreeWalker.ControlViewWalker.GetParent(current, cacheRequest);
            if (parent is null)
            {
                return null;
            }

            immediateParent ??= parent;
            int ordinal = TryResolveChildOrdinal(parent, current, cacheRequest);
            if (ordinal < 0)
            {
                return null;
            }

            ordinals.Add(ordinal);
            if (ElementsEqual(parent, rootNode.Element, cacheRequest))
            {
                ordinals.Reverse();
                return new FocusedElementPathResolution(
                    "0/" + string.Join("/", ordinals),
                    immediateParent);
            }

            current = parent;
        }
    }

    private static int TryResolveChildOrdinal(AutomationElement parent, AutomationElement child, CacheRequest cacheRequest)
    {
        int ordinal = 0;
        for (AutomationElement? candidate = TreeWalker.ControlViewWalker.GetFirstChild(parent, cacheRequest);
            candidate is not null;
            candidate = TreeWalker.ControlViewWalker.GetNextSibling(candidate, cacheRequest))
        {
            if (ElementsEqual(candidate, child, cacheRequest))
            {
                return ordinal;
            }

            ordinal++;
        }

        return -1;
    }

    private static bool ElementsEqual(AutomationElement left, AutomationElement right, CacheRequest cacheRequest)
    {
        UiaSnapshotNodeData leftData = new AutomationSnapshotNode(left, cacheRequest).GetData();
        UiaSnapshotNodeData rightData = new AutomationSnapshotNode(right, cacheRequest).GetData();

        if (RuntimeIdsEqual(leftData.RuntimeId, rightData.RuntimeId))
        {
            return true;
        }

        return leftData.NativeWindowHandle == rightData.NativeWindowHandle
            && string.Equals(leftData.AutomationId, rightData.AutomationId, StringComparison.Ordinal)
            && string.Equals(leftData.Name, rightData.Name, StringComparison.Ordinal)
            && string.Equals(leftData.ControlType, rightData.ControlType, StringComparison.Ordinal)
            && Equals(leftData.BoundingRectangle, rightData.BoundingRectangle);
    }

    private static bool RuntimeIdsEqual(int[]? left, int[]? right)
    {
        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateFocusedElementId(int[]? runtimeId, string path) =>
        runtimeId is { Length: > 0 }
            ? "rid:" + string.Join(".", runtimeId) + ";path:" + path
            : "path:" + path;

    private static string GetParentPath(string path)
    {
        int separatorIndex = path.LastIndexOf('/');
        return separatorIndex <= 0 ? "0" : path[..separatorIndex];
    }

    private static int GetDepth(string path) =>
        path.Count(character => character == '/');

    private static int GetOrdinal(string path)
    {
        int separatorIndex = path.LastIndexOf('/');
        if (separatorIndex < 0)
        {
            return 0;
        }

        return int.TryParse(path[(separatorIndex + 1)..], out int ordinal) ? ordinal : 0;
    }

    private static string? TryGetValueText(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObject)
                || patternObject is not ValuePattern valuePattern)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(valuePattern.Current.Value) ? null : valuePattern.Current.Value;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static string? TryGetTextPatternText(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object patternObject)
                || patternObject is not TextPattern textPattern)
            {
                return null;
            }

            string text = textPattern.DocumentRange.GetText(-1);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static UiAutomationWaitProbeResult Failed(string reason) =>
        new()
        {
            Reason = reason,
        };

    private readonly record struct TraversalNode(
        AutomationSnapshotNode Node,
        string? ParentElementId,
        int Depth,
        int Ordinal,
        string Path);

    private enum FocusProbeOutcome
    {
        NotMatched,
        Matched,
        TransientUnavailable,
        Failed,
    }

    private readonly record struct FocusProbeAttemptResult(
        FocusProbeOutcome Outcome,
        UiaElementSnapshot? Match,
        string? FailureReason = null);

    private sealed record FocusedElementPathResolution(
        string Path,
        AutomationElement? ImmediateParentElement);
}
