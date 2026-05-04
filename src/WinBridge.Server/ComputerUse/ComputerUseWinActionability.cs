// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinActionability
{
    private const int MaxSemanticHintLength = 256;

    public static bool IsClickActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.BoundingRectangle is not null
            && !node.IsOffscreen
            && node.IsEnabled;
    }

    public static bool IsClickActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Bounds is not null
            && element.Actions.Contains(ToolNames.ComputerUseWinClick, StringComparer.Ordinal);
    }

    public static bool IsSetValueActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && SupportsWritableValue(node);
    }

    public static bool IsSetValueActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Actions.Contains(ToolNames.ComputerUseWinSetValue, StringComparer.Ordinal);
    }

    public static bool IsScrollActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && node.Patterns.Contains("scroll", StringComparer.Ordinal);
    }

    public static bool IsScrollActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Actions.Contains(ToolNames.ComputerUseWinScroll, StringComparer.Ordinal);
    }

    public static bool IsDragEndpointActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.BoundingRectangle is not null
            && node.IsEnabled
            && !node.IsOffscreen;
    }

    public static bool IsDragEndpointActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Bounds is not null
            && element.Actions.Contains(ToolNames.ComputerUseWinDrag, StringComparer.Ordinal);
    }

    public static bool IsPerformSecondaryActionActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && ComputerUseWinSecondaryActionResolver.TryResolveActionKind(node.Patterns, out _);
    }

    public static bool IsPerformSecondaryActionActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Actions.Contains(ToolNames.ComputerUseWinPerformSecondaryAction, StringComparer.Ordinal)
            && ComputerUseWinSecondaryActionResolver.TryResolveActionKind(element.Patterns, out _);
    }

    public static bool IsTypeTextActionable(UiaElementSnapshot node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.IsEnabled
            && !node.IsOffscreen
            && node.HasKeyboardFocus
            && IsEditableTextControl(node.ControlType)
            && SupportsWritableValue(node);
    }

    public static bool IsTypeTextActionable(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.HasKeyboardFocus
            && IsEditableTextControl(element.ControlType)
            && element.Actions.Contains(ToolNames.ComputerUseWinTypeText, StringComparer.Ordinal);
    }

    public static bool IsFocusedTypeTextFallbackCandidate(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.HasKeyboardFocus
            && element.Bounds is not null
            && IsFocusedTextEntryFallbackControl(element)
            && element.Actions.Contains(ToolNames.ComputerUseWinClick, StringComparer.Ordinal);
    }

    public static bool HasSemanticFallbackSignal(ComputerUseWinStoredElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return !string.IsNullOrWhiteSpace(element.AutomationId);
    }

    private static bool IsFocusedTextEntryFallbackControl(ComputerUseWinStoredElement element)
    {
        if (string.Equals(element.ControlType, "edit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (string.Equals(element.ControlType, "document", StringComparison.OrdinalIgnoreCase)
                || string.Equals(element.ControlType, "custom", StringComparison.OrdinalIgnoreCase))
            && HasTextEntryHint(element);
    }

    private static bool HasTextEntryHint(ComputerUseWinStoredElement element)
    {
        return HasTextEntryHint(element.Name)
            || HasTextEntryHint(element.AutomationId);
    }

    private static bool HasTextEntryHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > MaxSemanticHintLength)
        {
            return false;
        }

        List<string> tokens = TokenizeSemanticName(trimmed);
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (token is "text" or "input" or "edit" or "query" or "textbox" or "textarea" or "textinput" or "searchbox")
            {
                return true;
            }

            if (index + 1 < tokens.Count && IsTextEntryQualifier(token) && IsTextEntryObject(tokens[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> TokenizeSemanticName(string value)
    {
        List<string> tokens = [];
        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        char previous = '\0';

        foreach (char current in value)
        {
            if (!char.IsLetterOrDigit(current))
            {
                FlushToken(tokens, buffer, ref length);
                previous = '\0';
                continue;
            }

            if (length > 0 && char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                FlushToken(tokens, buffer, ref length);
            }

            buffer[length++] = char.ToLowerInvariant(current);
            previous = current;
        }

        FlushToken(tokens, buffer, ref length);
        return tokens;
    }

    private static bool IsTextEntryQualifier(string token) =>
        token is "text" or "input" or "edit" or "query" or "search";

    private static bool IsTextEntryObject(string token) =>
        token is "box" or "field" or "input" or "textbox" or "textarea";

    private static void FlushToken(List<string> tokens, Span<char> buffer, ref int length)
    {
        if (length == 0)
        {
            return;
        }

        tokens.Add(new string(buffer[..length]));
        length = 0;
    }

    private static bool IsEditableTextControl(string controlType) =>
        string.Equals(controlType, "edit", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsWritableValue(UiaElementSnapshot node) =>
        node.IsReadOnly is false
        && (node.Patterns.Contains("value", StringComparer.Ordinal)
            || node.Patterns.Contains("range_value", StringComparer.Ordinal));
}
