// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Guards;

internal sealed class RuntimeGuardService(
    IMonitorManager monitorManager,
    IRuntimeGuardPlatform platform,
    ICaptureGuardFactSource captureFactSource,
    IUiaGuardFactSource uiaFactSource,
    TimeProvider timeProvider) : IRuntimeGuardService
{
    public RuntimeGuardAssessment GetSnapshot()
    {
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        RuntimeGuardRawFacts facts = platform.Probe() with
        {
            Capture = captureFactSource.GetFacts(),
            Uia = uiaFactSource.GetFacts(),
        };
        DateTimeOffset capturedAtUtc = timeProvider.GetUtcNow();
        ReadinessDomainStatus[] domains = RuntimeGuardPolicy.BuildDomains(facts);
        CapabilityGuardSummary[] capabilities = RuntimeGuardPolicy.BuildCapabilities(facts, topology, domains);
        RuntimeReadinessSnapshot readiness = new(
            CapturedAtUtc: capturedAtUtc,
            Domains: domains,
            Capabilities: capabilities);

        return new RuntimeGuardAssessment(
            Topology: topology,
            Readiness: readiness,
            BlockedCapabilities: RuntimeGuardPolicy.BuildBlockedCapabilities(capabilities),
            Warnings: RuntimeGuardPolicy.BuildWarnings(readiness));
    }
}
