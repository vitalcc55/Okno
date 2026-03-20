using System.Text;
using System.Windows;
using System.Windows.Automation;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class AutomationSnapshotNode(AutomationElement element, CacheRequest cacheRequest) : IUiaSnapshotNode
{
    private static readonly (AutomationProperty Property, string Literal)[] PatternAvailabilityProperties =
    [
        (AutomationElementIdentifiers.IsExpandCollapsePatternAvailableProperty, "expand_collapse"),
        (AutomationElementIdentifiers.IsGridItemPatternAvailableProperty, "grid_item"),
        (AutomationElementIdentifiers.IsGridPatternAvailableProperty, "grid"),
        (AutomationElementIdentifiers.IsInvokePatternAvailableProperty, "invoke"),
        (AutomationElementIdentifiers.IsItemContainerPatternAvailableProperty, "item_container"),
        (AutomationElementIdentifiers.IsMultipleViewPatternAvailableProperty, "multiple_view"),
        (AutomationElementIdentifiers.IsRangeValuePatternAvailableProperty, "range_value"),
        (AutomationElementIdentifiers.IsScrollItemPatternAvailableProperty, "scroll_item"),
        (AutomationElementIdentifiers.IsScrollPatternAvailableProperty, "scroll"),
        (AutomationElementIdentifiers.IsSelectionItemPatternAvailableProperty, "selection_item"),
        (AutomationElementIdentifiers.IsSelectionPatternAvailableProperty, "selection"),
        (AutomationElementIdentifiers.IsSynchronizedInputPatternAvailableProperty, "synchronized_input"),
        (AutomationElementIdentifiers.IsTableItemPatternAvailableProperty, "table_item"),
        (AutomationElementIdentifiers.IsTablePatternAvailableProperty, "table"),
        (AutomationElementIdentifiers.IsTextPatternAvailableProperty, "text"),
        (AutomationElementIdentifiers.IsTogglePatternAvailableProperty, "toggle"),
        (AutomationElementIdentifiers.IsTransformPatternAvailableProperty, "transform"),
        (AutomationElementIdentifiers.IsValuePatternAvailableProperty, "value"),
        (AutomationElementIdentifiers.IsVirtualizedItemPatternAvailableProperty, "virtualized_item"),
        (AutomationElementIdentifiers.IsWindowPatternAvailableProperty, "window"),
    ];

    public AutomationElement Element => element;

    public static CacheRequest CreateControlViewCacheRequest()
    {
        CacheRequest cacheRequest = new()
        {
            AutomationElementMode = AutomationElementMode.Full,
            TreeFilter = Automation.ControlViewCondition,
            TreeScope = TreeScope.Element,
        };

        cacheRequest.Add(AutomationElementIdentifiers.NameProperty);
        cacheRequest.Add(AutomationElementIdentifiers.AutomationIdProperty);
        cacheRequest.Add(AutomationElementIdentifiers.ClassNameProperty);
        cacheRequest.Add(AutomationElementIdentifiers.FrameworkIdProperty);
        cacheRequest.Add(AutomationElementIdentifiers.ControlTypeProperty);
        cacheRequest.Add(AutomationElementIdentifiers.LocalizedControlTypeProperty);
        cacheRequest.Add(AutomationElementIdentifiers.IsControlElementProperty);
        cacheRequest.Add(AutomationElementIdentifiers.IsContentElementProperty);
        cacheRequest.Add(AutomationElementIdentifiers.IsEnabledProperty);
        cacheRequest.Add(AutomationElementIdentifiers.IsOffscreenProperty);
        cacheRequest.Add(AutomationElementIdentifiers.HasKeyboardFocusProperty);
        cacheRequest.Add(AutomationElementIdentifiers.ProcessIdProperty);
        cacheRequest.Add(AutomationElementIdentifiers.BoundingRectangleProperty);
        cacheRequest.Add(AutomationElementIdentifiers.NativeWindowHandleProperty);
        cacheRequest.Add(AutomationElementIdentifiers.IsPasswordProperty);

        foreach ((AutomationProperty property, _) in PatternAvailabilityProperties)
        {
            cacheRequest.Add(property);
        }

        return cacheRequest;
    }

    public UiaSnapshotNodeData GetData()
    {
        ControlType? controlType = GetCachedControlType(element, AutomationElementIdentifiers.ControlTypeProperty);

        return new(
            RuntimeId: TryGetRuntimeId(element),
            Name: GetCachedString(element, AutomationElementIdentifiers.NameProperty),
            AutomationId: GetCachedString(element, AutomationElementIdentifiers.AutomationIdProperty),
            ClassName: GetCachedString(element, AutomationElementIdentifiers.ClassNameProperty),
            FrameworkId: GetCachedString(element, AutomationElementIdentifiers.FrameworkIdProperty),
            ControlType: NormalizeControlType(controlType),
            ControlTypeId: controlType?.Id ?? 0,
            LocalizedControlType: GetCachedString(element, AutomationElementIdentifiers.LocalizedControlTypeProperty),
            IsControlElement: GetCachedBoolean(element, AutomationElementIdentifiers.IsControlElementProperty),
            IsContentElement: GetCachedBoolean(element, AutomationElementIdentifiers.IsContentElementProperty),
            IsEnabled: GetCachedBoolean(element, AutomationElementIdentifiers.IsEnabledProperty),
            IsOffscreen: GetCachedBoolean(element, AutomationElementIdentifiers.IsOffscreenProperty),
            HasKeyboardFocus: GetCachedBoolean(element, AutomationElementIdentifiers.HasKeyboardFocusProperty),
            IsPassword: GetCachedBoolean(element, AutomationElementIdentifiers.IsPasswordProperty),
            Patterns: GetAvailablePatterns(element),
            BoundingRectangle: GetCachedBounds(element, AutomationElementIdentifiers.BoundingRectangleProperty),
            NativeWindowHandle: GetCachedNativeWindowHandle(element, AutomationElementIdentifiers.NativeWindowHandleProperty));
    }

    public IUiaSnapshotNode? GetFirstChild()
    {
        AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(element, cacheRequest);
        return child is null ? null : new AutomationSnapshotNode(child, cacheRequest);
    }

    public IUiaSnapshotNode? GetNextSibling()
    {
        AutomationElement? sibling = TreeWalker.ControlViewWalker.GetNextSibling(element, cacheRequest);
        return sibling is null ? null : new AutomationSnapshotNode(sibling, cacheRequest);
    }

    internal static string NormalizeControlType(ControlType? controlType)
    {
        if (controlType is null)
        {
            return "unknown";
        }

        string raw = controlType.ProgrammaticName;
        int separatorIndex = raw.LastIndexOf('.');
        string token = separatorIndex >= 0 ? raw[(separatorIndex + 1)..] : raw;
        return ConvertPascalToSnakeCase(token);
    }

    private static string ConvertPascalToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static int[]? TryGetRuntimeId(AutomationElement automationElement)
    {
        try
        {
            int[] runtimeId = automationElement.GetRuntimeId();
            return runtimeId.Length == 0 ? null : runtimeId;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string[] GetAvailablePatterns(AutomationElement automationElement)
    {
        List<string> patterns = [];
        foreach ((AutomationProperty property, string literal) in PatternAvailabilityProperties)
        {
            if (GetCachedBoolean(automationElement, property))
            {
                patterns.Add(literal);
            }
        }

        return patterns.ToArray();
    }

    private static string? GetCachedString(AutomationElement automationElement, AutomationProperty property)
    {
        object value = automationElement.GetCachedPropertyValue(property, true);
        return value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;
    }

    private static bool GetCachedBoolean(AutomationElement automationElement, AutomationProperty property)
    {
        object value = automationElement.GetCachedPropertyValue(property, true);
        return value is bool flag && flag;
    }

    private static ControlType? GetCachedControlType(AutomationElement automationElement, AutomationProperty property)
    {
        object value = automationElement.GetCachedPropertyValue(property, true);
        return value as ControlType;
    }

    private static long? GetCachedNativeWindowHandle(AutomationElement automationElement, AutomationProperty property)
    {
        object value = automationElement.GetCachedPropertyValue(property, true);
        return value is int handle && handle > 0 ? handle : null;
    }

    private static Bounds? GetCachedBounds(AutomationElement automationElement, AutomationProperty property)
    {
        object value = automationElement.GetCachedPropertyValue(property, true);
        if (value is not Rect rect || rect.IsEmpty)
        {
            return null;
        }

        double right = rect.X + rect.Width;
        double bottom = rect.Y + rect.Height;
        if (double.IsNaN(rect.X) || double.IsNaN(rect.Y) || double.IsNaN(right) || double.IsNaN(bottom))
        {
            return null;
        }

        if (double.IsInfinity(rect.X) || double.IsInfinity(rect.Y) || double.IsInfinity(right) || double.IsInfinity(bottom))
        {
            return null;
        }

        return new Bounds(
            (int)Math.Floor(rect.X),
            (int)Math.Floor(rect.Y),
            (int)Math.Ceiling(right),
            (int)Math.Ceiling(bottom));
    }
}
