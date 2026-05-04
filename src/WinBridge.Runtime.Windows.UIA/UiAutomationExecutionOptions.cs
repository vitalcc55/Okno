// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.UIA;

internal sealed record UiAutomationExecutionOptions(TimeSpan? Timeout)
{
    public static UiAutomationExecutionOptions Default { get; } = new(TimeSpan.FromSeconds(3));

    public static UiAutomationExecutionOptions Unbounded { get; } = new((TimeSpan?)null);
}
