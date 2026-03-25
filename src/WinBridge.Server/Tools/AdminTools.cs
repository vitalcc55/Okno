using System.ComponentModel;
using ModelContextProtocol.Server;
using WinBridge.Runtime;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;
using WinBridge.Runtime.Session;
using WinBridge.Runtime.Tooling;
using WinBridge.Runtime.Windows.Display;

namespace WinBridge.Server.Tools;

[McpServerToolType]
public sealed class AdminTools
{
    private static readonly string[] ReadinessDomains =
    [
        ReadinessDomainValues.DesktopSession,
        ReadinessDomainValues.SessionAlignment,
        ReadinessDomainValues.Integrity,
        ReadinessDomainValues.UiAccess,
    ];

    private static readonly string[] ObserveCapabilities =
    [
        CapabilitySummaryValues.Capture,
        CapabilitySummaryValues.Uia,
        CapabilitySummaryValues.Wait,
    ];

    private static readonly string[] DeferredCapabilities =
    [
        CapabilitySummaryValues.Input,
        CapabilitySummaryValues.Clipboard,
        CapabilitySummaryValues.Launch,
    ];

    private readonly AuditLog _auditLog;
    private readonly IMonitorManager _monitorManager;
    private readonly RuntimeInfo _runtimeInfo;
    private readonly ISessionManager _sessionManager;

    public AdminTools(AuditLog auditLog, RuntimeInfo runtimeInfo, ISessionManager sessionManager, IMonitorManager monitorManager)
    {
        _auditLog = auditLog;
        _monitorManager = monitorManager;
        _runtimeInfo = runtimeInfo;
        _sessionManager = sessionManager;
    }

    [Description(ToolDescriptions.OknoHealthTool)]
    [McpServerTool(Name = ToolNames.OknoHealth)]
    public HealthResult Health()
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.OknoHealth,
            new { probe = "health" },
            invocation =>
            {
                DisplayTopologySnapshot topology = _monitorManager.GetTopologySnapshot();
                RuntimeReadinessSnapshot readiness = CreateReadinessSnapshot();
                CapabilityGuardSummary[] blockedCapabilities = readiness.Capabilities
                    .Where(capability => string.Equals(capability.Status, GuardStatusValues.Blocked, StringComparison.Ordinal))
                    .ToArray();

                HealthResult result = new(
                    Service: _runtimeInfo.ServiceName,
                    Version: _runtimeInfo.Version,
                    Transport: _runtimeInfo.Transport,
                    AuditSchemaVersion: _runtimeInfo.AuditSchemaVersion,
                    RunId: _runtimeInfo.RunId,
                    ArtifactsDirectory: _runtimeInfo.ArtifactsDirectory,
                    ActiveMonitorCount: topology.Monitors.Count,
                    DisplayIdentity: topology.Diagnostics,
                    ImplementedTools: ToolContractManifest.ImplementedNames,
                    DeferredTools: ToolContractManifest.DeferredPhaseMap,
                    Readiness: readiness,
                    BlockedCapabilities: blockedCapabilities,
                    Warnings: [CreateTopLevelReadinessWarning()]);

                invocation.Complete("done", "Возвращена сводка состояния runtime и консервативный readiness snapshot.");
                return result;
            });

    [Description(ToolDescriptions.OknoContractTool)]
    [McpServerTool(Name = ToolNames.OknoContract)]
    public ContractSummaryResult Contract()
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.OknoContract,
            null,
            invocation =>
            {
                ContractSummaryResult result = new(
                    ImplementedTools: ToolContractManifest.Implemented.Select(ContractToolDescriptorFactory.FromToolDescriptor).ToArray(),
                    DeferredTools: ToolContractManifest.Deferred.Select(ContractToolDescriptorFactory.FromToolDescriptor).ToArray(),
                    Notes: ToolContractManifest.ContractNotes);

                invocation.Complete("done", "Возвращён текущий MCP contract runtime.");
                return result;
            });

    [Description(ToolDescriptions.OknoSessionStateTool)]
    [McpServerTool(Name = ToolNames.OknoSessionState)]
    public SessionSnapshot SessionState()
        => ToolExecution.Run(
            _auditLog,
            _sessionManager.GetSnapshot(),
            ToolNames.OknoSessionState,
            null,
            invocation =>
            {
                SessionSnapshot snapshot = _sessionManager.GetSnapshot();
                invocation.Complete("done", "Возвращён текущий session snapshot.", snapshot.AttachedWindow?.Window.Hwnd);
                return snapshot;
            });

    private static RuntimeReadinessSnapshot CreateReadinessSnapshot()
    {
        ReadinessDomainStatus[] domains = ReadinessDomains
            .Select(CreateUnknownDomain)
            .ToArray();

        CapabilityGuardSummary[] capabilities =
        [
            .. ObserveCapabilities.Select(CreateUnknownCapability),
            .. DeferredCapabilities.Select(CreateBlockedCapability),
        ];

        return new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Domains: domains,
            Capabilities: capabilities);
    }

    private static ReadinessDomainStatus CreateUnknownDomain(string domain)
        => new(
            Domain: domain,
            Status: GuardStatusValues.Unknown,
            Reasons: [CreateAssessmentNotImplementedReason(domain)]);

    private static CapabilityGuardSummary CreateUnknownCapability(string capability)
        => new(
            Capability: capability,
            Status: GuardStatusValues.Unknown,
            Reasons: [CreateAssessmentNotImplementedReason(capability)]);

    private static CapabilityGuardSummary CreateBlockedCapability(string capability)
        => new(
            Capability: capability,
            Status: GuardStatusValues.Blocked,
            Reasons: [CreateCapabilityNotImplementedReason(capability)]);

    private static GuardReason CreateAssessmentNotImplementedReason(string source)
        => new(
            Code: GuardReasonCodeValues.AssessmentNotImplemented,
            Severity: GuardSeverityValues.Warning,
            MessageHuman: "Runtime guard assessment для этого домена или capability пока не реализован; статус остаётся консервативно unknown.",
            Source: source);

    private static GuardReason CreateCapabilityNotImplementedReason(string source)
        => new(
            Code: GuardReasonCodeValues.CapabilityNotImplemented,
            Severity: GuardSeverityValues.Blocked,
            MessageHuman: "Эта capability пока не реализована в текущем runtime surface и не может считаться готовой.",
            Source: source);

    private static GuardReason CreateTopLevelReadinessWarning()
        => new(
            Code: GuardReasonCodeValues.AssessmentNotImplemented,
            Severity: GuardSeverityValues.Warning,
            MessageHuman: "Runtime readiness snapshot пока публикуется в contract-first режиме: guard derivations для desktop session, session alignment, integrity и uiaccess ещё не реализованы.",
            Source: ToolNames.OknoHealth);
}
