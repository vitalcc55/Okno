// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class UiaSnapshotRequestValidator
{
    public const int MaxNodesCeiling = 1024;

    public static bool TryValidate(UiaSnapshotRequest request, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Depth < 0)
        {
            reason = "Параметр depth для UIA snapshot должен быть >= 0.";
            return false;
        }

        if (request.MaxNodes < 1)
        {
            reason = "Параметр maxNodes для UIA snapshot должен быть >= 1.";
            return false;
        }

        if (request.MaxNodes > MaxNodesCeiling)
        {
            reason = $"Параметр maxNodes для UIA snapshot должен быть <= {MaxNodesCeiling}.";
            return false;
        }

        reason = null;
        return true;
    }
}
