using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Runtime.Guards;

public sealed record RuntimeGuardAssessment(
    DisplayTopologySnapshot Topology,
    RuntimeReadinessSnapshot Readiness,
    IReadOnlyList<CapabilityGuardSummary> BlockedCapabilities,
    IReadOnlyList<GuardReason> Warnings);
