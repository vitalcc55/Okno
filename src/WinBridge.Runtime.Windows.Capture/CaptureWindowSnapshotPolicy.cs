// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Capture;

internal static class CaptureWindowSnapshotPolicy
{
    public static WindowDescriptor BuildRefreshedWindowSnapshot(
        WindowDescriptor requestWindow,
        WindowDescriptor? liveWindow,
        Bounds frameBounds,
        int effectiveDpi,
        double dpiScale,
        MonitorInfo? monitor)
    {
        WindowDescriptor sourceWindow = liveWindow is not null && liveWindow.Hwnd == requestWindow.Hwnd
            ? liveWindow
            : requestWindow with
            {
                ProcessId = null,
                ThreadId = null,
                ClassName = null,
            };

        return sourceWindow with
        {
            Bounds = frameBounds,
            EffectiveDpi = effectiveDpi,
            DpiScale = dpiScale,
            MonitorId = monitor?.Descriptor.MonitorId,
            MonitorFriendlyName = monitor?.Descriptor.FriendlyName,
        };
    }
}
