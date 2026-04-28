using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class Win32UiAutomationSecondaryActionService : IUiAutomationSecondaryActionService
{
    public Task<UiaSecondaryActionResult> ExecuteAsync(
        WindowDescriptor targetWindow,
        UiaSecondaryActionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetWindow);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            CacheRequest cacheRequest = AutomationSnapshotNode.CreateControlViewCacheRequest();
            using (cacheRequest.Activate())
            {
                AutomationElement root = AutomationElement.FromHandle(new IntPtr(targetWindow.Hwnd));
                if (!UiAutomationElementResolver.TryResolveElement(root, cacheRequest, request.ElementId, out AutomationElement? element)
                    || element is null)
                {
                    return Task.FromResult(UiaSecondaryActionResult.FailureResult(
                        request.ActionKind,
                        UiaSecondaryActionFailureKindValues.MissingElement,
                        "UI Automation больше не смогла разрешить target element для secondary semantic path."));
                }

                UiaSnapshotNodeData data = new AutomationSnapshotNode(element, cacheRequest).GetData();
                if (!data.IsEnabled)
                {
                    return Task.FromResult(UiaSecondaryActionResult.FailureResult(
                        request.ActionKind,
                        UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                        "Элемент больше не находится в enabled состоянии для secondary semantic path."));
                }

                return request.ActionKind switch
                {
                    UiaSecondaryActionKindValues.Toggle => Task.FromResult(ExecuteToggle(element)),
                    UiaSecondaryActionKindValues.ExpandCollapse => Task.FromResult(ExecuteExpandCollapse(element)),
                    _ => Task.FromResult(UiaSecondaryActionResult.FailureResult(
                        request.ActionKind,
                        UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                        $"Runtime не поддерживает secondary action kind '{request.ActionKind}'.")),
                };
            }
        }
        catch (ElementNotAvailableException)
        {
            return Task.FromResult(UiaSecondaryActionResult.FailureResult(
                request.ActionKind,
                UiaSecondaryActionFailureKindValues.MissingElement,
                "UI Automation потеряла target element до secondary semantic dispatch."));
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(UiaSecondaryActionResult.FailureResult(
                request.ActionKind,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(UiaSecondaryActionResult.FailureResult(
                request.ActionKind,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message));
        }
        catch (COMException exception)
        {
            return Task.FromResult(UiaSecondaryActionResult.FailureResult(
                request.ActionKind,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message));
        }
    }

    private static UiaSecondaryActionResult ExecuteToggle(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object patternObject)
            || patternObject is not TogglePattern togglePattern)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.Toggle,
                UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                "Элемент не поддерживает TogglePattern.");
        }

        ToggleState before = togglePattern.Current.ToggleState;
        try
        {
            togglePattern.Toggle();
        }
        catch (ElementNotEnabledException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.Toggle,
                UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                exception.Message,
                resolvedPattern: "toggle_pattern");
        }
        catch (ArgumentException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.Toggle,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "toggle_pattern");
        }
        catch (InvalidOperationException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.Toggle,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "toggle_pattern");
        }
        catch (COMException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.Toggle,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "toggle_pattern");
        }

        ToggleState after = togglePattern.Current.ToggleState;
        return after != before
            ? UiaSecondaryActionResult.SuccessResult(UiaSecondaryActionKindValues.Toggle, "toggle_pattern")
            : UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.Toggle,
                UiaSecondaryActionFailureKindValues.NoStateChange,
                "TogglePattern не подтвердил изменение state после secondary action.",
                resolvedPattern: "toggle_pattern");
    }

    private static UiaSecondaryActionResult ExecuteExpandCollapse(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object patternObject)
            || patternObject is not ExpandCollapsePattern expandCollapsePattern)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                "Элемент не поддерживает ExpandCollapsePattern.");
        }

        ExpandCollapseState before = expandCollapsePattern.Current.ExpandCollapseState;
        if (before == ExpandCollapseState.LeafNode)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                "Leaf node не поддерживает secondary expand/collapse action.",
                resolvedPattern: "expand_collapse_pattern");
        }

        try
        {
            if (before is ExpandCollapseState.Collapsed or ExpandCollapseState.PartiallyExpanded)
            {
                expandCollapsePattern.Expand();
            }
            else
            {
                expandCollapsePattern.Collapse();
            }
        }
        catch (ElementNotEnabledException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.UnsupportedPattern,
                exception.Message,
                resolvedPattern: "expand_collapse_pattern");
        }
        catch (ArgumentException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "expand_collapse_pattern");
        }
        catch (InvalidOperationException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "expand_collapse_pattern");
        }
        catch (COMException exception)
        {
            return UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "expand_collapse_pattern");
        }

        ExpandCollapseState after = expandCollapsePattern.Current.ExpandCollapseState;
        return after != before
            ? UiaSecondaryActionResult.SuccessResult(UiaSecondaryActionKindValues.ExpandCollapse, "expand_collapse_pattern")
            : UiaSecondaryActionResult.FailureResult(
                UiaSecondaryActionKindValues.ExpandCollapse,
                UiaSecondaryActionFailureKindValues.NoStateChange,
                "ExpandCollapsePattern не подтвердил изменение state после secondary action.",
                resolvedPattern: "expand_collapse_pattern");
    }
}
