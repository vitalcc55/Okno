namespace WinBridge.Runtime.Contracts;

public sealed record HealthResult(
    string Service,
    string Version,
    string Transport,
    string AuditSchemaVersion,
    string RunId,
    string ArtifactsDirectory,
    int ActiveMonitorCount,
    DisplayIdentityDiagnostics DisplayIdentity,
    IReadOnlyList<string> ImplementedTools,
    IReadOnlyDictionary<string, string> DeferredTools,
    RuntimeReadinessSnapshot Readiness,
    IReadOnlyList<CapabilityGuardSummary> BlockedCapabilities,
    IReadOnlyList<GuardReason> Warnings);
