// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Capture;

internal enum WgcAcquisitionFailureAction
{
    FallbackToDesktopGdi,
    ThrowToolError,
}

internal static class WgcAcquisitionFailurePolicy
{
    public static WgcAcquisitionFailureAction Evaluate(CaptureScope scope) =>
        scope == CaptureScope.Desktop
            ? WgcAcquisitionFailureAction.FallbackToDesktopGdi
            : WgcAcquisitionFailureAction.ThrowToolError;
}
