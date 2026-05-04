// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using Windows.Graphics.Capture;
using WinBridge.Runtime.Guards;

namespace WinBridge.Runtime;

internal sealed class GraphicsCaptureGuardFactSource : ICaptureGuardFactSource
{
    public CaptureCapabilityProbeResult GetFacts() =>
        new(
            FactResolved: true,
            WindowsGraphicsCaptureSupported: GraphicsCaptureSession.IsSupported());
}
