using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class Win32UiAutomationScrollService : IUiAutomationScrollService
{
    public Task<UiaScrollResult> ScrollAsync(
        WindowDescriptor targetWindow,
        UiaScrollRequest request,
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
                    return Task.FromResult(UiaScrollResult.FailureResult(
                        UiaScrollFailureKindValues.MissingElement,
                        "UI Automation больше не смогла разрешить target element для semantic scroll path."));
                }

                if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out object patternObject)
                    || patternObject is not ScrollPattern scrollPattern)
                {
                    return Task.FromResult(UiaScrollResult.FailureResult(
                        UiaScrollFailureKindValues.UnsupportedPattern,
                        "Элемент не поддерживает ScrollPattern."));
                }

                bool vertical = string.Equals(request.Direction, UiaScrollDirectionValues.Up, StringComparison.Ordinal)
                    || string.Equals(request.Direction, UiaScrollDirectionValues.Down, StringComparison.Ordinal);
                bool horizontal = string.Equals(request.Direction, UiaScrollDirectionValues.Left, StringComparison.Ordinal)
                    || string.Equals(request.Direction, UiaScrollDirectionValues.Right, StringComparison.Ordinal);
                if (!vertical && !horizontal)
                {
                    return Task.FromResult(UiaScrollResult.FailureResult(
                        UiaScrollFailureKindValues.UnsupportedPattern,
                        $"Неподдерживаемое направление semantic scroll '{request.Direction}'."));
                }

                if ((vertical && !scrollPattern.Current.VerticallyScrollable)
                    || (horizontal && !scrollPattern.Current.HorizontallyScrollable))
                {
                    return Task.FromResult(UiaScrollResult.FailureResult(
                        UiaScrollFailureKindValues.UnsupportedPattern,
                        "Элемент не поддерживает requested semantic scroll direction.",
                        resolvedPattern: "scroll_pattern"));
                }

                double before = vertical
                    ? scrollPattern.Current.VerticalScrollPercent
                    : scrollPattern.Current.HorizontalScrollPercent;
                ScrollAmount horizontalAmount = ResolveHorizontalAmount(request.Direction);
                ScrollAmount verticalAmount = ResolveVerticalAmount(request.Direction);

                for (int index = 0; index < request.Pages; index++)
                {
                    scrollPattern.Scroll(horizontalAmount, verticalAmount);
                }

                double after = vertical
                    ? scrollPattern.Current.VerticalScrollPercent
                    : scrollPattern.Current.HorizontalScrollPercent;
                bool moved = Math.Abs(after - before) > 0.000001d;
                return Task.FromResult(
                    moved
                        ? UiaScrollResult.SuccessResult("scroll_pattern", movementObserved: true)
                        : UiaScrollResult.FailureResult(
                            UiaScrollFailureKindValues.NoMovement,
                            "ScrollPattern не подтвердил изменение позиции после semantic scroll dispatch.",
                            resolvedPattern: "scroll_pattern"));
            }
        }
        catch (ElementNotAvailableException)
        {
            return Task.FromResult(UiaScrollResult.FailureResult(
                UiaScrollFailureKindValues.MissingElement,
                "UI Automation потеряла target element до semantic scroll dispatch."));
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(UiaScrollResult.FailureResult(
                UiaScrollFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "scroll_pattern"));
        }
        catch (ArgumentException exception)
        {
            return Task.FromResult(UiaScrollResult.FailureResult(
                UiaScrollFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "scroll_pattern"));
        }
        catch (COMException exception)
        {
            return Task.FromResult(UiaScrollResult.FailureResult(
                UiaScrollFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "scroll_pattern"));
        }
    }

    private static ScrollAmount ResolveHorizontalAmount(string direction) =>
        direction switch
        {
            UiaScrollDirectionValues.Left => ScrollAmount.LargeDecrement,
            UiaScrollDirectionValues.Right => ScrollAmount.LargeIncrement,
            _ => ScrollAmount.NoAmount,
        };

    private static ScrollAmount ResolveVerticalAmount(string direction) =>
        direction switch
        {
            UiaScrollDirectionValues.Up => ScrollAmount.LargeDecrement,
            UiaScrollDirectionValues.Down => ScrollAmount.LargeIncrement,
            _ => ScrollAmount.NoAmount,
        };
}
