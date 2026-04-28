using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Globalization;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

public sealed class Win32UiAutomationSetValueService : IUiAutomationSetValueService
{
    public Task<UiaSetValueResult> SetValueAsync(
        WindowDescriptor targetWindow,
        UiaSetValueRequest request,
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
                    return Task.FromResult(UiaSetValueResult.FailureResult(
                        UiaSetValueFailureKindValues.MissingElement,
                        "UI Automation больше не смогла разрешить target element для semantic set path."));
                }

                UiaSnapshotNodeData data = new AutomationSnapshotNode(element, cacheRequest).GetData();
                if (!data.IsEnabled)
                {
                    return Task.FromResult(UiaSetValueResult.FailureResult(
                        UiaSetValueFailureKindValues.UnsupportedPattern,
                        "Элемент больше не находится в enabled состоянии для semantic set path."));
                }

                return string.Equals(request.ValueKind, UiaSetValueKindValues.Text, StringComparison.Ordinal)
                    ? Task.FromResult(SetTextValue(element, request))
                    : Task.FromResult(SetNumericValue(element, request));
            }
        }
        catch (ElementNotAvailableException)
        {
            return Task.FromResult(UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.MissingElement,
                "UI Automation потеряла target element до semantic set dispatch."));
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                exception.Message));
        }
        catch (COMException exception)
        {
            return Task.FromResult(UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                exception.Message));
        }
    }

    private static UiaSetValueResult SetTextValue(AutomationElement element, UiaSetValueRequest request)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObject)
            || patternObject is not ValuePattern valuePattern)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.UnsupportedPattern,
                "Элемент не поддерживает ValuePattern.",
                resolvedPattern: null);
        }

        if (valuePattern.Current.IsReadOnly)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.ReadOnly,
                "ValuePattern target находится в read-only состоянии.",
                resolvedPattern: "value_pattern");
        }

        string requestedValue = request.TextValue ?? string.Empty;
        try
        {
            valuePattern.SetValue(requestedValue);
        }
        catch (ElementNotEnabledException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.UnsupportedPattern,
                exception.Message,
                resolvedPattern: "value_pattern");
        }
        catch (ArgumentException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.InvalidValue,
                exception.Message,
                resolvedPattern: "value_pattern");
        }
        catch (InvalidOperationException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "value_pattern");
        }
        catch (COMException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "value_pattern");
        }

        string currentValue = valuePattern.Current.Value ?? string.Empty;
        return string.Equals(currentValue, requestedValue, StringComparison.Ordinal)
            ? UiaSetValueResult.SuccessResult("value_pattern")
            : UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                "ValuePattern не подтвердил ожидаемое значение после SetValue.",
                resolvedPattern: "value_pattern");
    }

    private static UiaSetValueResult SetNumericValue(AutomationElement element, UiaSetValueRequest request)
    {
        if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out object patternObject)
            && patternObject is RangeValuePattern rangeValuePattern)
        {
            if (rangeValuePattern.Current.IsReadOnly)
            {
                return UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.ReadOnly,
                    "RangeValuePattern target находится в read-only состоянии.",
                    resolvedPattern: "range_value_pattern");
            }

            double requestedValue = request.NumberValue ?? 0d;
            if (requestedValue < rangeValuePattern.Current.Minimum || requestedValue > rangeValuePattern.Current.Maximum)
            {
                return UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.ValueOutOfRange,
                    "RangeValuePattern target не принимает requested numeric value.",
                    resolvedPattern: "range_value_pattern");
            }

            try
            {
                rangeValuePattern.SetValue(requestedValue);
            }
            catch (ElementNotEnabledException exception)
            {
                return UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.UnsupportedPattern,
                    exception.Message,
                    resolvedPattern: "range_value_pattern");
            }
            catch (ArgumentException exception)
            {
                return UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.ValueOutOfRange,
                    exception.Message,
                    resolvedPattern: "range_value_pattern");
            }
            catch (InvalidOperationException exception)
            {
                return UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.DispatchFailed,
                    exception.Message,
                    resolvedPattern: "range_value_pattern");
            }
            catch (COMException exception)
            {
                return UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.DispatchFailed,
                    exception.Message,
                    resolvedPattern: "range_value_pattern");
            }

            return Math.Abs(rangeValuePattern.Current.Value - requestedValue) <= 0.000001d
                ? UiaSetValueResult.SuccessResult("range_value_pattern")
                : UiaSetValueResult.FailureResult(
                    UiaSetValueFailureKindValues.DispatchFailed,
                    "RangeValuePattern не подтвердил ожидаемое значение после SetValue.",
                    resolvedPattern: "range_value_pattern");
        }

        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePatternObject)
            || valuePatternObject is not ValuePattern valuePattern)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.UnsupportedPattern,
                "Элемент не поддерживает ни RangeValuePattern, ни numeric-compatible ValuePattern.",
                resolvedPattern: null);
        }

        if (valuePattern.Current.IsReadOnly)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.ReadOnly,
                "ValuePattern target находится в read-only состоянии для numeric semantic set.",
                resolvedPattern: "value_pattern");
        }

        double requestedNumericValue = request.NumberValue ?? 0d;
        string requestedValueText = requestedNumericValue.ToString(CultureInfo.InvariantCulture);
        try
        {
            valuePattern.SetValue(requestedValueText);
        }
        catch (ElementNotEnabledException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.UnsupportedPattern,
                exception.Message,
                resolvedPattern: "value_pattern");
        }
        catch (ArgumentException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.InvalidValue,
                exception.Message,
                resolvedPattern: "value_pattern");
        }
        catch (InvalidOperationException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "value_pattern");
        }
        catch (COMException exception)
        {
            return UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                exception.Message,
                resolvedPattern: "value_pattern");
        }

        string currentValue = valuePattern.Current.Value ?? string.Empty;
        return double.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedCurrentValue)
            && Math.Abs(parsedCurrentValue - requestedNumericValue) <= 0.000001d
            ? UiaSetValueResult.SuccessResult("value_pattern")
            : UiaSetValueResult.FailureResult(
                UiaSetValueFailureKindValues.DispatchFailed,
                "ValuePattern не подтвердил ожидаемое numeric value после SetValue.",
                resolvedPattern: "value_pattern");
    }
}
