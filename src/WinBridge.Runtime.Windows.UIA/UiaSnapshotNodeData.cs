using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiaSnapshotNodeData(
    int[]? RuntimeId,
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? FrameworkId,
    string ControlType,
    int ControlTypeId,
    string? LocalizedControlType,
    bool IsControlElement,
    bool IsContentElement,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool IsPassword,
    bool? IsReadOnly,
    string[] Patterns,
    Bounds? BoundingRectangle,
    long? NativeWindowHandle);
