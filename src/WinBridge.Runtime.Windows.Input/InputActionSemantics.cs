// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal static class InputActionSemantics
{
    public static bool IsMove(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.Move, StringComparison.Ordinal);

    public static bool IsClick(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.Click, StringComparison.Ordinal);

    public static bool IsDoubleClick(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.DoubleClick, StringComparison.Ordinal);

    public static bool IsScroll(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.Scroll, StringComparison.Ordinal);

    public static bool IsDrag(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.Drag, StringComparison.Ordinal);

    public static bool IsType(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.Type, StringComparison.Ordinal);

    public static bool IsKeypress(InputAction action) =>
        string.Equals(action.Type, InputActionTypeValues.Keypress, StringComparison.Ordinal);

    public static bool RequiresResolvedPoint(InputAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action.Point is not null
            || action.Path is not null
            || IsMove(action)
            || IsClick(action)
            || IsDoubleClick(action)
            || IsScroll(action)
            || IsDrag(action);
    }

    public static string ResolveEffectiveButton(InputAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return string.IsNullOrWhiteSpace(action.Button) ? InputButtonValues.Left : action.Button!;
    }

    public static string? ResolveEffectiveButtonForAction(InputAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action.Type switch
        {
            InputActionTypeValues.Move => null,
            InputActionTypeValues.DoubleClick => InputButtonValues.Left,
            InputActionTypeValues.Click => ResolveEffectiveButton(action),
            _ => action.Button,
        };
    }

    public static IReadOnlyList<string>? ResolveActionKeys(InputAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action.Keys is { Count: > 0 })
        {
            return action.Keys;
        }

        return string.IsNullOrWhiteSpace(action.Key)
            ? null
            : [action.Key];
    }

    public static bool SamePoint(InputPoint left, InputPoint right) =>
        left.X == right.X && left.Y == right.Y;
}
