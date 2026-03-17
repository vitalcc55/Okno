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
                    DeferredTools: ToolContractManifest.DeferredPhaseMap);

                invocation.Complete("done", "Возвращена сводка состояния runtime.");
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
}
