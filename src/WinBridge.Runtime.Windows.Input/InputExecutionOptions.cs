// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Input;

internal sealed record InputExecutionOptions
{
    public static InputExecutionOptions Default { get; } = new(TimeSpan.FromMilliseconds(50));

    public InputExecutionOptions(TimeSpan doubleClickDelay)
    {
        if (doubleClickDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(doubleClickDelay), doubleClickDelay, "Double-click delay не может быть отрицательным.");
        }

        DoubleClickDelay = doubleClickDelay;
    }

    public TimeSpan DoubleClickDelay { get; }
}
