using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Guards;

internal sealed class RuntimeGuardService(
    IMonitorManager monitorManager,
    IRuntimeGuardPlatform platform,
    TimeProvider timeProvider) : IRuntimeGuardService
{
    public RuntimeGuardAssessment GetSnapshot()
    {
        DisplayTopologySnapshot topology = monitorManager.GetTopologySnapshot();
        RuntimeGuardRawFacts facts = platform.Probe();
        DateTimeOffset capturedAtUtc = timeProvider.GetUtcNow();
        ReadinessDomainStatus[] domains = RuntimeGuardPolicy.BuildDomains(facts);
        CapabilityGuardSummary[] capabilities = RuntimeGuardPolicy.BuildCapabilities();
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
