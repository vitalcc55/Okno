// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class InputActionTypeValues
{
    public const string Move = "move";
    public const string Click = "click";
    public const string DoubleClick = "double_click";
    public const string Drag = "drag";
    public const string Scroll = "scroll";
    public const string Type = "type";
    public const string Keypress = "keypress";

    public static IReadOnlySet<string> StructuralFreeze { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Move,
            Click,
            DoubleClick,
            Drag,
            Scroll,
            Type,
            Keypress,
        };

    public static IReadOnlySet<string> ClickFirstSubset { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Move,
            Click,
            DoubleClick,
        };
}
