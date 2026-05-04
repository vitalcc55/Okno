// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.UIA;

internal static class UiaElementIdBuilder
{
    public static string Create(int[]? runtimeId, string path)
    {
        if (runtimeId is { Length: > 0 })
        {
            return "rid:" + string.Join(".", runtimeId);
        }

        return "path:" + path;
    }
}
