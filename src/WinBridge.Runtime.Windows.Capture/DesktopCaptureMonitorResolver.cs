// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Windows.Capture;

internal static class DesktopCaptureMonitorResolver
{
    public static MonitorInfo? Resolve(
        WindowDescriptor? window,
        string? explicitMonitorId,
        IMonitorManager monitorManager,
        DisplayTopologySnapshot topology)
    {
        return !string.IsNullOrWhiteSpace(explicitMonitorId)
            ? monitorManager.FindMonitorById(explicitMonitorId, topology)
            : window is null
                ? monitorManager.GetPrimaryMonitor(topology)
                : monitorManager.FindMonitorForWindow(window.Hwnd, topology);
    }
}
