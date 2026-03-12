using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime;

public sealed class RuntimeInfo(AuditLogOptions auditOptions)
{
    public string ServiceName { get; } = AuditConstants.ServiceName;

    public string Version { get; } =
        typeof(RuntimeInfo).Assembly.GetName().Version?.ToString() ?? "0.1.0.0";

    public string Transport { get; } = "stdio";

    public string AuditSchemaVersion { get; } = AuditConstants.SchemaVersion;

    public string RunId => auditOptions.RunId;

    public string ArtifactsDirectory => auditOptions.RunDirectory;
}
