// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

internal enum CaptureBackend
{
    WindowsGraphicsCapture,
    DesktopGdiFallback,
    Unsupported,
}

internal static class CaptureBackendSelector
{
    public static CaptureBackend Select(CaptureScope scope, bool isWindowsGraphicsCaptureSupported) =>
        scope switch
        {
            CaptureScope.Window when isWindowsGraphicsCaptureSupported => CaptureBackend.WindowsGraphicsCapture,
            CaptureScope.Window => CaptureBackend.Unsupported,
            CaptureScope.Desktop when isWindowsGraphicsCaptureSupported => CaptureBackend.WindowsGraphicsCapture,
            CaptureScope.Desktop => CaptureBackend.DesktopGdiFallback,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };
}
