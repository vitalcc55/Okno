// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Windows.Launch;

public sealed record ProcessLaunchOptions(
    TimeSpan MainWindowPollInterval,
    TimeSpan InputIdleWaitSlice)
{
    public static ProcessLaunchOptions Default { get; } = new(
        MainWindowPollInterval: TimeSpan.FromMilliseconds(100),
        InputIdleWaitSlice: TimeSpan.FromMilliseconds(100));
}
