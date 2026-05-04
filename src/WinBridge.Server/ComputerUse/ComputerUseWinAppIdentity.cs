// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinAppIdentity
{
    public static bool TryCreateStableAppId(WindowDescriptor window, out string? appId)
    {
        ArgumentNullException.ThrowIfNull(window);

        appId = NormalizeProcessIdentity(window.ProcessName);
        return !string.IsNullOrWhiteSpace(appId);
    }

    public static string CreateAppId(WindowDescriptor window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (TryCreateStableAppId(window, out string? processName))
        {
            return processName!;
        }

        return $"hwnd-{window.Hwnd.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string NormalizeAppId(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        string trimmed = appId.Trim();
        if (trimmed.StartsWith("hwnd-", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToLowerInvariant();
        }

        return NormalizeProcessIdentity(trimmed) ?? trimmed.ToLowerInvariant();
    }

    public static string? NormalizeProcessIdentity(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        string candidate = processName.Trim().Replace('\\', '/');
        int separatorIndex = candidate.LastIndexOf('/');
        if (separatorIndex >= 0)
        {
            candidate = candidate[(separatorIndex + 1)..];
        }

        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4];
        }

        return string.IsNullOrWhiteSpace(candidate)
            ? null
            : candidate.ToLowerInvariant();
    }
}
