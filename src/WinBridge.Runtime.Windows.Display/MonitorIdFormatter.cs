// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Display;

public static class MonitorIdFormatter
{
    public static string FromDisplaySource(int highPart, uint lowPart, uint sourceId)
    {
        uint high = unchecked((uint)highPart);
        return $"display-source:{high:x8}{lowPart:x8}:{sourceId}";
    }

    public static string FromGdiDeviceName(string gdiDeviceName)
    {
        string token = NormalizeGdiDeviceName(gdiDeviceName).Replace(@"\\.\", string.Empty, StringComparison.OrdinalIgnoreCase);
        return "gdi:" + token.ToLowerInvariant();
    }

    public static bool IsGdiMonitorId(string monitorId) =>
        !string.IsNullOrWhiteSpace(monitorId)
        && monitorId.StartsWith("gdi:", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeGdiDeviceName(string gdiDeviceName) =>
        (gdiDeviceName ?? string.Empty).Trim().ToUpperInvariant();
}
