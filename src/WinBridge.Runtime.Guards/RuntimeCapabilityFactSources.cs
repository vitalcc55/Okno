// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Guards;

internal interface ICaptureGuardFactSource
{
    CaptureCapabilityProbeResult GetFacts();
}

internal interface IUiaGuardFactSource
{
    UiaCapabilityProbeResult GetFacts();
}

internal sealed record CaptureCapabilityProbeResult(
    bool FactResolved,
    bool WindowsGraphicsCaptureSupported);

internal sealed record UiaCapabilityProbeResult(
    bool FactResolved,
    bool WorkerLaunchSpecResolved,
    string? FailureReason);

internal sealed class DefaultCaptureGuardFactSource : ICaptureGuardFactSource
{
    public CaptureCapabilityProbeResult GetFacts() =>
        new(
            FactResolved: false,
            WindowsGraphicsCaptureSupported: false);
}

internal sealed class DefaultUiaGuardFactSource : IUiaGuardFactSource
{
    public UiaCapabilityProbeResult GetFacts() =>
        new(
            FactResolved: false,
            WorkerLaunchSpecResolved: false,
            FailureReason: null);
}
