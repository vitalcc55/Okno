// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Guards;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class UiaWorkerGuardFactSource : IUiaGuardFactSource
{
    public UiaCapabilityProbeResult GetFacts()
    {
        try
        {
            _ = ProcessIsolatedUiAutomationBackend.ResolveWorkerLaunchSpec();
            return new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: true,
                FailureReason: null);
        }
        catch (FileNotFoundException exception)
        {
            return new UiaCapabilityProbeResult(
                FactResolved: true,
                WorkerLaunchSpecResolved: false,
                FailureReason: exception.Message);
        }
        catch
        {
            return new UiaCapabilityProbeResult(
                FactResolved: false,
                WorkerLaunchSpecResolved: false,
                FailureReason: null);
        }
    }
}
