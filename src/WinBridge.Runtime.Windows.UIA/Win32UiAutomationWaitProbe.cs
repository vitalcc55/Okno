using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class Win32UiAutomationWaitProbe : IUiAutomationWaitProbe
{
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

            List<UiaElementSnapshot> matches = [];
            string? matchedText = null;
            string? matchedTextSource = null;

            Stack<TraversalNode> pending = new();
            pending.Push(new(rootNode, null, 0, 0, "0"));

            while (pending.Count > 0 && matches.Count < 2)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TraversalNode current = pending.Pop();
                UiaSnapshotNodeData data = current.Node.GetData();
                string elementId = UiaElementIdBuilder.Create(data.RuntimeId, current.Path);

                if (SelectorMatches(data, request.Selector))
                {
                    UiaElementSnapshot snapshot = CreateLeafSnapshot(data, elementId, current.ParentElementId, current.Depth, current.Ordinal);
                    matches.Add(snapshot);

                    if (string.Equals(request.Condition, WaitConditionValues.TextAppears, StringComparison.Ordinal)
                        && request.ExpectedText is string expectedText)
                    {
                        WaitTextCandidateMatch? textMatch = UiAutomationWaitTextCandidateResolver.Match(
                            expectedText,
                            TryGetValueText(current.Node.Element),
                            TryGetTextPatternText(current.Node.Element),
                            snapshot.Name);
                        if (textMatch is not null)
                        {
                            matchedText = textMatch.Text;
                            matchedTextSource = textMatch.Source;
                        }
                    }
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

            return new UiAutomationWaitProbeResult
            {
                Window = observedWindow,
                Matches = matches.ToArray(),
                MatchedText = matches.Count == 1 ? matchedText : null,
                MatchedTextSource = matches.Count == 1 ? matchedTextSource : null,
            };
        }
        catch (InvalidOperationException exception)
        {
            return Failed(exception.Message);
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
}
