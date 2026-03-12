namespace WinBridge.Runtime.Contracts;

public sealed record HealthResult(
    string Service,
    string Version,
    string Transport,
    string AuditSchemaVersion,
    string RunId,
    string ArtifactsDirectory,
    IReadOnlyList<string> ImplementedTools,
    IReadOnlyDictionary<string, string> DeferredTools);
