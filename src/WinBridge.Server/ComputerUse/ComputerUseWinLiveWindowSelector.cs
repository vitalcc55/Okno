// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal static class ComputerUseWinLiveWindowSelector
{
    public static bool TrySelectSingle(
        IReadOnlyList<WindowDescriptor> windows,
        Func<WindowDescriptor, bool> predicate,
        out WindowDescriptor? selectedWindow,
        out bool ambiguous)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(predicate);

        selectedWindow = null;
        ambiguous = false;

        foreach (WindowDescriptor window in windows)
        {
            if (!predicate(window))
            {
                continue;
            }

            if (selectedWindow is not null)
            {
                selectedWindow = null;
                ambiguous = true;
                return false;
            }

            selectedWindow = window;
        }

        return selectedWindow is not null;
    }
}
