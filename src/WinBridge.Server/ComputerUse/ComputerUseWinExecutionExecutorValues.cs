// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinExecutionExecutorDescriptor(
    string Value,
    string DefaultDispatchClass,
    bool UsesPhysicalPointer,
    bool UsesPhysicalKeyboard,
    bool MovesSystemCursor);

internal static class ComputerUseWinExecutionExecutorValues
{
    public const string UiaToggle = "uia_toggle";
    public const string UiaTogglePattern = "uia_toggle_pattern";
    public const string UiaExpandCollapse = "uia_expand_collapse";
    public const string UiaExpandCollapsePattern = "uia_expand_collapse_pattern";
    public const string UiaInvoke = "uia_invoke";
    public const string UiaInvokePattern = "uia_invoke_pattern";
    public const string UiaScrollPattern = "uia_scroll_pattern";
    public const string UiaValuePattern = "uia_value_pattern";
    public const string UiaRangeValuePattern = "uia_range_value_pattern";
    public const string UiaSemanticSet = "uia_semantic_set";

    public const string Win32PointerClick = "win32_pointer_click";
    public const string FreshUiaRevalidationToInput = "fresh_uia_revalidation_to_input";
    public const string FreshUiaRevalidationToInputDrag = "fresh_uia_revalidation_to_input_drag";
    public const string CapturePixelsInput = "capture_pixels_input";
    public const string ScreenInput = "screen_input";
    public const string Win32SendInputKeypress = "win32_sendinput_keypress";
    public const string Win32SendInputDrag = "win32_sendinput_drag";
    public const string CapturePixelsDragInput = "capture_pixels_drag_input";
    public const string ScreenDragInput = "screen_drag_input";
    public const string Win32SendInputWheel = "win32_sendinput_wheel";
    public const string Win32SendInputUnicode = "win32_sendinput_unicode";
    public const string CapturePixelsTextInput = "capture_pixels_text_input";

    private static readonly ComputerUseWinExecutionExecutorDescriptor[] DescriptorRegistry =
    [
        new(UiaToggle, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaTogglePattern, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaExpandCollapse, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaExpandCollapsePattern, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaInvoke, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaInvokePattern, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaScrollPattern, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaValuePattern, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaRangeValuePattern, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(UiaSemanticSet, ComputerUseWinDispatchClassValues.Semantic, false, false, false),
        new(Win32PointerClick, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(FreshUiaRevalidationToInput, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(FreshUiaRevalidationToInputDrag, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(CapturePixelsInput, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(ScreenInput, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(Win32SendInputKeypress, ComputerUseWinDispatchClassValues.ExpectedPhysical, false, true, false),
        new(Win32SendInputDrag, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(CapturePixelsDragInput, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(ScreenDragInput, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, true),
        new(Win32SendInputWheel, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, false, false),
        new(Win32SendInputUnicode, ComputerUseWinDispatchClassValues.ExpectedPhysical, false, true, false),
        new(CapturePixelsTextInput, ComputerUseWinDispatchClassValues.ExpectedPhysical, true, true, true),
    ];

    private static readonly Dictionary<string, ComputerUseWinExecutionExecutorDescriptor> DescriptorMap =
        DescriptorRegistry.ToDictionary(descriptor => descriptor.Value, StringComparer.Ordinal);

    public static IReadOnlyList<string> All { get; } =
        DescriptorRegistry.Select(descriptor => descriptor.Value).ToArray();

    public static IReadOnlyList<ComputerUseWinExecutionExecutorDescriptor> SupportedDescriptors { get; } =
        DescriptorRegistry;

    public static bool TryGetDescriptor(
        string? executor,
        [NotNullWhen(true)] out ComputerUseWinExecutionExecutorDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(executor))
        {
            descriptor = null;
            return false;
        }

        return DescriptorMap.TryGetValue(executor, out descriptor);
    }

    public static string ResolveSetValue(string? resolvedPattern) =>
        resolvedPattern switch
        {
            "value_pattern" => UiaValuePattern,
            "range_value_pattern" => UiaRangeValuePattern,
            null => UiaSemanticSet,
            _ => throw UnknownResolvedPattern("set_value", resolvedPattern),
        };

    public static string ResolveScroll(string? resolvedPattern) =>
        resolvedPattern switch
        {
            null or "scroll_pattern" => UiaScrollPattern,
            _ => throw UnknownResolvedPattern("scroll", resolvedPattern),
        };

    public static string ResolveSecondaryAction(string actionKind, string? resolvedPattern)
    {
        if (!string.IsNullOrWhiteSpace(resolvedPattern))
        {
            return resolvedPattern switch
            {
                "toggle_pattern" => UiaTogglePattern,
                "expand_collapse_pattern" => UiaExpandCollapsePattern,
                _ => throw UnknownResolvedPattern("perform_secondary_action", resolvedPattern),
            };
        }

        return actionKind switch
        {
            UiaSecondaryActionKindValues.Toggle => UiaToggle,
            UiaSecondaryActionKindValues.ExpandCollapse => UiaExpandCollapse,
            _ => throw new InvalidOperationException($"Unknown secondary action kind '{actionKind}' cannot be mapped into execution facts vocabulary."),
        };
    }

    public static string? ResolveSecondaryActionKind(string? executor) =>
        executor switch
        {
            UiaToggle or UiaTogglePattern => UiaSecondaryActionKindValues.Toggle,
            UiaExpandCollapse or UiaExpandCollapsePattern => UiaSecondaryActionKindValues.ExpandCollapse,
            _ => null,
        };

    private static InvalidOperationException UnknownResolvedPattern(string actionName, string resolvedPattern) =>
        new($"Execution facts vocabulary does not know resolved pattern '{resolvedPattern}' for action '{actionName}'.");
}
