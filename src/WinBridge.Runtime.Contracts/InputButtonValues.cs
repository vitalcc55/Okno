// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class InputButtonValues
{
    public const string Left = "left";
    public const string Right = "right";
    public const string Middle = "middle";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Left,
            Right,
            Middle,
        };
}
